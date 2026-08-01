using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public sealed class ModrinthModpackService
{
    private const string SearchUrl = "https://api.modrinth.com/v2/search";
    private const string ProjectUrl = "https://api.modrinth.com/v2/project";

    public async Task<IReadOnlyList<ModpackListItem>> SearchPopularAsync(
        string? query = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var facets = Uri.EscapeDataString("[[\"project_type:modpack\"]]");
        var q = string.IsNullOrWhiteSpace(query) ? "" : $"&query={Uri.EscapeDataString(query.Trim())}";
        var clamped = Math.Clamp(limit, 1, 100);
        var url = $"{SearchUrl}?facets={facets}&index=downloads&limit={clamped}{q}";

        var json = await HttpRetryHelper.GetStringAsync(AppHttp.Client, url, cancellationToken);
        var root = JObject.Parse(json);
        var hits = root["hits"] as JArray;
        if (hits == null || hits.Count == 0)
            return Array.Empty<ModpackListItem>();

        return hits
            .OfType<JObject>()
            .Select(ParseHit)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToList();
    }

    public async Task<(string VersionId, string DownloadUrl, string FileName)> ResolveLatestPackFileAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{ProjectUrl}/{Uri.EscapeDataString(projectId)}/version";
        var json = await HttpRetryHelper.GetStringAsync(AppHttp.Client, url, cancellationToken);
        var versions = JArray.Parse(json);
        if (versions.Count == 0)
            throw new InvalidOperationException(LocalizationService.T("add_build.modpack.no_versions"));

        var version = versions[0] as JObject
            ?? throw new InvalidOperationException(LocalizationService.T("add_build.modpack.no_versions"));

        var versionId = version["id"]?.ToString() ?? "";
        var files = version["files"] as JArray;
        var primary = files?
            .OfType<JObject>()
            .FirstOrDefault(f => f["primary"]?.Value<bool>() == true)
            ?? files?.OfType<JObject>().FirstOrDefault();

        var downloadUrl = primary?["url"]?.ToString();
        var fileName = primary?["filename"]?.ToString() ?? $"{projectId}.mrpack";

        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new InvalidOperationException(LocalizationService.T("add_build.modpack.download_url_missing"));

        return (versionId, downloadUrl, fileName);
    }

    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        DirectoryEnsureParent(destinationPath);
        await using var stream = await AppHttp.Client.GetStreamAsync(url, cancellationToken);
        await using var file = System.IO.File.Create(destinationPath);
        await stream.CopyToAsync(file, cancellationToken);
    }

    private static ModpackListItem ParseHit(JObject hit)
    {
        var versions = hit["versions"] as JArray;
        var categories = hit["categories"] as JArray;
        var loaders = categories?
            .Select(c => c?.ToString())
            .Where(c => c is "fabric" or "forge" or "neoforge" or "quilt")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string?>();

        var gameVersions = versions?
            .Select(v => v?.ToString())
            .Where(v => !string.IsNullOrEmpty(v))
            .Take(2)
            .ToList() ?? new List<string?>();

        return new ModpackListItem
        {
            Source = ModpackSource.Modrinth,
            Id = hit["project_id"]?.ToString() ?? hit["slug"]?.ToString() ?? "",
            Name = hit["title"]?.ToString() ?? "",
            Summary = hit["description"]?.ToString() ?? "",
            Downloads = hit["downloads"]?.Value<long>() ?? 0,
            Author = hit["author"]?.ToString() ?? "",
            IconUrl = hit["icon_url"]?.ToString(),
            LatestVersionId = hit["latest_version"]?.ToString(),
            GameVersionsHint = gameVersions.Count > 0 ? string.Join(", ", gameVersions!) : null,
            LoadersHint = loaders.Count > 0 ? string.Join(", ", loaders!) : null
        };
    }

    private static void DirectoryEnsureParent(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);
    }
}
