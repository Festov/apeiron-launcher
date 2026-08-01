using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public sealed class CurseForgeModpackService
{
    public const int MinecraftGameId = 432;
    public const int ModpackClassId = 4471;

    private const string BaseUrl = "https://api.curseforge.com/v1";
    private readonly string _apiKey;

    /// <summary>Separate client: no auto-redirect so we can re-attach the API key on each hop.</summary>
    private static readonly HttpClient DownloadClient = CreateDownloadClient();

    public CurseForgeModpackService(string apiKey)
    {
        _apiKey = apiKey?.Trim() ?? "";
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    private static HttpClient CreateDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(45),
            MaxConnectionsPerServer = 8,
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppHttp.UserAgent);
        return client;
    }

    public async Task<IReadOnlyList<ModpackListItem>> SearchPopularAsync(
        string? query = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKey();

        var target = Math.Clamp(limit, 1, 100);
        var search = string.IsNullOrWhiteSpace(query) ? "" : $"&searchFilter={Uri.EscapeDataString(query.Trim())}";
        var results = new List<ModpackListItem>();
        var index = 0;
        const int maxPage = 50;

        while (results.Count < target)
        {
            var pageSize = Math.Min(maxPage, target - results.Count);
            // sortField=2 Popularity, sortOrder=desc
            var url =
                $"{BaseUrl}/mods/search?gameId={MinecraftGameId}&classId={ModpackClassId}" +
                $"&sortField=2&sortOrder=desc&pageSize={pageSize}&index={index}{search}";

            var json = await GetStringAsync(url, cancellationToken);
            var root = JObject.Parse(json);
            var data = root["data"] as JArray;
            if (data == null || data.Count == 0)
                break;

            var page = data
                .OfType<JObject>()
                .Select(ParseMod)
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .ToList();

            if (page.Count == 0)
                break;

            results.AddRange(page);
            index += page.Count;

            if (page.Count < pageSize)
                break;
        }

        return results.Count > target ? results.Take(target).ToList() : results;
    }

    public async Task<(int ModId, int FileId, string FileName, string DownloadUrl)> ResolveLatestPackFileAsync(
        int modId,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKey();

        var url = $"{BaseUrl}/mods/{modId}/files?pageSize=5";
        var json = await GetStringAsync(url, cancellationToken);
        var root = JObject.Parse(json);
        var data = root["data"] as JArray;
        var file = data?.OfType<JObject>().FirstOrDefault(IsPackFile)
            ?? data?.OfType<JObject>().FirstOrDefault();

        if (file == null)
            throw new InvalidOperationException(LocalizationService.T("add_build.modpack.no_versions"));

        var fileId = file["id"]?.Value<int>() ?? 0;
        var fileName = file["fileName"]?.ToString() ?? $"{modId}.zip";
        var downloadUrl = file["downloadUrl"]?.ToString();

        if (string.IsNullOrWhiteSpace(downloadUrl))
            downloadUrl = await GetDownloadUrlAsync(modId, fileId, cancellationToken);

        if (string.IsNullOrWhiteSpace(downloadUrl))
            downloadUrl = BuildCdnDownloadUrl(fileId, fileName);

        if (string.IsNullOrWhiteSpace(downloadUrl) || fileId == 0)
            throw new InvalidOperationException(LocalizationService.T("add_build.modpack.download_url_missing"));

        return (modId, fileId, fileName, downloadUrl!);
    }

    public async Task<string> GetDownloadUrlAsync(
        int modId,
        int fileId,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKey();
        var url = $"{BaseUrl}/mods/{modId}/files/{fileId}/download-url";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await AppHttp.Client.SendAsync(request, cancellationToken);
        // Third-party distribution disabled → 403; caller should use BuildCdnDownloadUrl.
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return "";

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = JObject.Parse(json);
        return root["data"]?.ToString() ?? "";
    }

    /// <summary>
    /// Builds a direct edge.forgecdn.net URL. Needed when file.downloadUrl is null
    /// (third-party distribution disabled) — the /download-url API then returns 403.
    /// </summary>
    public static string BuildCdnDownloadUrl(int fileId, string fileName)
    {
        if (fileId <= 0 || string.IsNullOrWhiteSpace(fileName))
            return "";

        var safeName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safeName))
            return "";

        // CurseForge uses unpadded remainder: files/6963/18/name.jar
        var encoded = Uri.EscapeDataString(safeName);
        return $"https://edge.forgecdn.net/files/{fileId / 1000}/{fileId % 1000}/{encoded}";
    }

    /// <summary>Resolves many file download URLs in chunks (POST /v1/mods/files).</summary>
    public async Task<IReadOnlyDictionary<int, (string FileName, string DownloadUrl)>> GetFilesBulkAsync(
        IReadOnlyList<int> fileIds,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKey();
        var result = new Dictionary<int, (string FileName, string DownloadUrl)>();
        if (fileIds.Count == 0)
            return result;

        const int chunkSize = 50;
        for (var offset = 0; offset < fileIds.Count; offset += chunkSize)
        {
            var chunk = fileIds.Skip(offset).Take(chunkSize).ToList();
            var body = new JObject { ["fileIds"] = new JArray(chunk) }.ToString();
            var json = await PostStringAsync($"{BaseUrl}/mods/files", body, cancellationToken);
            var root = JObject.Parse(json);
            var data = root["data"] as JArray;
            if (data == null)
                continue;

            foreach (var file in data.OfType<JObject>())
            {
                var id = file["id"]?.Value<int>() ?? 0;
                if (id == 0)
                    continue;

                var fileName = file["fileName"]?.ToString() ?? $"{id}.jar";
                var downloadUrl = file["downloadUrl"]?.ToString() ?? "";
                result[id] = (fileName, downloadUrl);
            }
        }

        return result;
    }

    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKey();
        DirectoryEnsureParent(destinationPath);

        Exception? lastError = null;
        for (var attempt = 0; attempt <= HttpRetryHelper.DefaultMaxRetries + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadFollowingRedirectsAsync(url, destinationPath, cancellationToken);
                return;
            }
            catch (Exception ex) when (
                attempt < HttpRetryHelper.DefaultMaxRetries + 1 &&
                ex is not OperationCanceledException &&
                !cancellationToken.IsCancellationRequested &&
                (HttpRetryHelper.IsTransientException(ex) || IsForbidden(ex)))
            {
                lastError = ex;
                await Task.Delay(HttpRetryHelper.GetDownloadBackoff(attempt + 1, HttpStatusCode.Forbidden), cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw lastError ?? new HttpRequestException($"Failed to download: {url}");
    }

    /// <summary>Resolves a CDN URL then downloads with API key on every redirect hop.</summary>
    public async Task DownloadModFileAsync(
        int modId,
        int fileId,
        string destinationPath,
        string? preferredUrl = null,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKey();
        DirectoryEnsureParent(destinationPath);

        Exception? lastError = null;
        for (var attempt = 0; attempt <= HttpRetryHelper.DefaultMaxRetries + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = await ResolveDownloadUrlAsync(
                    modId,
                    fileId,
                    attempt == 0 ? preferredUrl : null,
                    fileName,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(url))
                    throw new HttpRequestException($"No download URL for {modId}:{fileId}");

                await DownloadFollowingRedirectsAsync(url, destinationPath, cancellationToken);
                return;
            }
            catch (Exception ex) when (
                attempt < HttpRetryHelper.DefaultMaxRetries + 1 &&
                ex is not OperationCanceledException &&
                !cancellationToken.IsCancellationRequested &&
                (HttpRetryHelper.IsTransientException(ex) || IsForbidden(ex)))
            {
                lastError = ex;
                preferredUrl = null;
                await Task.Delay(HttpRetryHelper.GetDownloadBackoff(attempt + 1, HttpStatusCode.Forbidden), cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw lastError ?? new HttpRequestException($"Failed to download mod {modId}:{fileId}");
    }

    private async Task<string> ResolveDownloadUrlAsync(
        int modId,
        int fileId,
        string? preferredUrl,
        string? fileName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(preferredUrl))
            return preferredUrl;

        // Prefer constructed CDN URL when we know the file name — avoids /download-url 403
        // for mods with third-party distribution disabled.
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var cdn = BuildCdnDownloadUrl(fileId, fileName);
            if (!string.IsNullOrWhiteSpace(cdn))
                return cdn;
        }

        var fromApi = await GetDownloadUrlAsync(modId, fileId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fromApi))
            return fromApi;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = await GetFileNameAsync(modId, fileId, cancellationToken);
            var cdn = BuildCdnDownloadUrl(fileId, fileName ?? "");
            if (!string.IsNullOrWhiteSpace(cdn))
                return cdn;
        }

        return "";
    }

    public async Task<string?> GetFileNameAsync(
        int modId,
        int fileId,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKey();
        var url = $"{BaseUrl}/mods/{modId}/files/{fileId}";
        var json = await GetStringAsync(url, cancellationToken);
        var root = JObject.Parse(json);
        return root["data"]?["fileName"]?.ToString();
    }

    private async Task DownloadFollowingRedirectsAsync(
        string startUrl,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var currentUrl = startUrl;
        for (var hop = 0; hop < 12; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestUrl = AppendApiKeyIfNeeded(currentUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);

            using var response = await DownloadClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location == null)
                    throw new HttpRequestException($"Redirect without Location from {currentUrl}");

                currentUrl = location.IsAbsoluteUri
                    ? location.ToString()
                    : new Uri(new Uri(currentUrl), location).ToString();
                continue;
            }

            if (HttpRetryHelper.IsTransientStatusCode(response.StatusCode))
                throw new HttpRequestException(
                    $"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).",
                    null,
                    response.StatusCode);

            response.EnsureSuccessStatusCode();

            var tempPath = destinationPath + ".partial";
            try
            {
                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var file = File.Create(tempPath))
                    await stream.CopyToAsync(file, cancellationToken);

                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                File.Move(tempPath, destinationPath);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }

            return;
        }

        throw new HttpRequestException($"Too many redirects for {startUrl}");
    }

    private static bool IsForbidden(Exception ex) =>
        ex is HttpRequestException http &&
        (http.StatusCode == HttpStatusCode.Forbidden ||
         http.Message.Contains("403", StringComparison.Ordinal));

    private string AppendApiKeyIfNeeded(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(_apiKey))
            return url;

        if (!IsCurseForgeHost(url))
            return url;

        if (url.Contains("api-key=", StringComparison.OrdinalIgnoreCase))
            return url;

        var sep = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return url + sep + "api-key=" + Uri.EscapeDataString(_apiKey);
    }

    private static bool IsCurseForgeHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Contains("forgecdn.net", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("curseforge.com", StringComparison.OrdinalIgnoreCase);

        var host = uri.Host;
        return host.Equals("api.curseforge.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("www.curseforge.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".forgecdn.net", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("forgecdn.net", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> PostStringAsync(string url, string jsonBody, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

                using var response = await AppHttp.Client.SendAsync(request, cancellationToken);
                if (HttpRetryHelper.IsTransientStatusCode(response.StatusCode) && attempt < HttpRetryHelper.DefaultMaxRetries)
                {
                    await Task.Delay(HttpRetryHelper.GetBackoff(attempt + 1), cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (
                attempt < HttpRetryHelper.DefaultMaxRetries &&
                ex is not OperationCanceledException &&
                HttpRetryHelper.IsTransientException(ex))
            {
                await Task.Delay(HttpRetryHelper.GetBackoff(attempt + 1), cancellationToken);
            }
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await AppHttp.Client.SendAsync(request, cancellationToken);
                if (HttpRetryHelper.IsTransientStatusCode(response.StatusCode) && attempt < HttpRetryHelper.DefaultMaxRetries)
                {
                    await Task.Delay(HttpRetryHelper.GetBackoff(attempt + 1), cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (
                attempt < HttpRetryHelper.DefaultMaxRetries &&
                ex is not OperationCanceledException &&
                HttpRetryHelper.IsTransientException(ex))
            {
                await Task.Delay(HttpRetryHelper.GetBackoff(attempt + 1), cancellationToken);
            }
        }
    }

    private void EnsureApiKey()
    {
        if (!HasApiKey)
            throw new InvalidOperationException(LocalizationService.T("add_build.modpack.curseforge_key_missing"));
    }

    private static bool IsPackFile(JObject file)
    {
        var name = file["fileName"]?.ToString() ?? "";
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static ModpackListItem ParseMod(JObject mod)
    {
        var id = mod["id"]?.Value<int>().ToString() ?? "";
        var authors = mod["authors"] as JArray;
        var author = authors?.OfType<JObject>().FirstOrDefault()?["name"]?.ToString() ?? "";
        var logo = mod["logo"]?["thumbnailUrl"]?.ToString() ?? mod["logo"]?["url"]?.ToString();
        var latestFilesIndexes = mod["latestFilesIndexes"] as JArray;
        var gameVersions = latestFilesIndexes?
            .OfType<JObject>()
            .Select(f => f["gameVersion"]?.ToString())
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct()
            .Take(2)
            .ToList() ?? new List<string?>();

        return new ModpackListItem
        {
            Source = ModpackSource.CurseForge,
            Id = id,
            Name = mod["name"]?.ToString() ?? "",
            Summary = mod["summary"]?.ToString() ?? "",
            Downloads = mod["downloadCount"]?.Value<long>() ?? 0,
            Author = author,
            IconUrl = logo,
            GameVersionsHint = gameVersions.Count > 0 ? string.Join(", ", gameVersions!) : null
        };
    }

    private static void DirectoryEnsureParent(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);
    }
}
