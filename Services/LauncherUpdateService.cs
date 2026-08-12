using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apeiron.Services;

public enum LauncherUpdateKind
{
    Zip,
    Exe,
    Patch
}

public sealed class LauncherUpdateInfo
{
    public Version LatestVersion { get; init; } = new(0, 0);
    public string DownloadUrl { get; init; } = "";
    public string? ExpectedSha256 { get; init; }
    public string ReleasePageUrl { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public LauncherUpdateKind Kind { get; init; } = LauncherUpdateKind.Zip;
}

public static class LauncherUpdateService
{
    public static Version GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static bool IsNewerVersion(Version latest, Version current)
    {
        if (latest.Major != current.Major) return latest.Major > current.Major;
        if (latest.Minor != current.Minor) return latest.Minor > current.Minor;
        if (latest.Build != current.Build) return latest.Build > current.Build;
        return latest.Revision > current.Revision;
    }

    public static async Task<LauncherUpdateInfo?> CheckForUpdateAsync(
        string? githubRepository,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(githubRepository))
            return null;

        var repo = githubRepository.Trim().Trim('/');
        var url = $"https://api.github.com/repos/{repo}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(AppHttp.UserAgent);

        using var response = await AppHttp.Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var versionText = tag.TrimStart('v', 'V');
        if (!Version.TryParse(NormalizeVersion(versionText), out var latestVersion))
            return null;

        var current = GetCurrentVersion();
        if (!IsNewerVersion(latestVersion, current))
            return null;

        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        var page = root.GetProperty("html_url").GetString() ?? "";

        if (root.TryGetProperty("assets", out var assets))
        {
            var patchName = $"Apeiron-{FormatVer(current)}-to-{FormatVer(latestVersion)}.patch";
            var patch = FindAsset(assets, patchName);
            if (patch.Url != null)
            {
                return new LauncherUpdateInfo
                {
                    LatestVersion = latestVersion,
                    DownloadUrl = patch.Url,
                    ExpectedSha256 = patch.Sha256,
                    ReleasePageUrl = page,
                    ReleaseNotes = notes,
                    Kind = LauncherUpdateKind.Patch
                };
            }

            var exeAsset = FindAsset(assets, "Apeiron.exe");
            if (exeAsset.Url == null)
                exeAsset = FindAssetEndsWith(assets, $"-{FormatVer(latestVersion)}-win-x64.exe");
            if (exeAsset.Url == null)
                exeAsset = FindAssetEndsWith(assets, "-win-x64.exe");
            if (exeAsset.Url != null && (exeAsset.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return new LauncherUpdateInfo
                {
                    LatestVersion = latestVersion,
                    DownloadUrl = exeAsset.Url,
                    ExpectedSha256 = exeAsset.Sha256,
                    ReleasePageUrl = page,
                    ReleaseNotes = notes,
                    Kind = LauncherUpdateKind.Exe
                };
            }
        }

        var (downloadUrl, sha256) = FindZipAsset(root);
        if (string.IsNullOrEmpty(downloadUrl))
            return null;

        return new LauncherUpdateInfo
        {
            LatestVersion = latestVersion,
            DownloadUrl = downloadUrl,
            ExpectedSha256 = sha256,
            ReleasePageUrl = page,
            ReleaseNotes = notes,
            Kind = LauncherUpdateKind.Zip
        };
    }

    public static async Task<string> DownloadUpdatePackageAsync(
        string downloadUrl,
        string? expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var ext = downloadUrl.Contains(".patch", StringComparison.OrdinalIgnoreCase) ? ".patch"
            : downloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ".exe"
            : ".zip";
        var tempPath = Path.Combine(Path.GetTempPath(), "apeiron-update-" + Guid.NewGuid().ToString("N") + ext);
        using var response = await AppHttp.Client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(tempPath);
        await input.CopyToAsync(output, cancellationToken);

        if (!string.IsNullOrWhiteSpace(expectedSha256))
            VerifySha256(tempPath, expectedSha256);

        return tempPath;
    }

    /// <summary>Returns path to a ready-to-apply Apeiron.exe (after zip extract or patch apply).</summary>
    public static string ResolveUpdatedExecutable(LauncherUpdateInfo update, string downloadedPath)
    {
        return update.Kind switch
        {
            LauncherUpdateKind.Zip => ExtractLauncherExecutable(downloadedPath),
            LauncherUpdateKind.Exe => downloadedPath,
            LauncherUpdateKind.Patch => ApplyPatchToCurrent(downloadedPath),
            _ => ExtractLauncherExecutable(downloadedPath)
        };
    }

    public static string ExtractLauncherExecutable(string zipPath)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), "apeiron-update-" + Guid.NewGuid().ToString("N"));
        ZipExtractHelper.ExtractZipFile(zipPath, extractDir);

        var exe = Directory.GetFiles(extractDir, "Apeiron.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(exe))
            throw new FileNotFoundException("Apeiron.exe not found in update package.");

        return exe;
    }

    public static string ApplyPatchToCurrent(string patchPath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Apeiron.exe");
        var newExe = Path.Combine(Path.GetTempPath(), "apeiron-patched-" + Guid.NewGuid().ToString("N") + ".exe");
        BinaryPatchHelper.ApplyPatch(currentExe, patchPath, newExe);
        return newExe;
    }

    public static void ScheduleApplyUpdate(string newExePath)
    {
        var launcherDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(launcherDir, "Apeiron.exe");

        var scriptPath = Path.Combine(Path.GetTempPath(), "apeiron-apply-update-" + Guid.NewGuid().ToString("N") + ".cmd");
        var script = $"""
            @echo off
            timeout /t 2 /nobreak >nul
            move /y "{currentExe}" "{currentExe}.old" >nul
            copy /y "{newExePath}" "{currentExe}" >nul
            start "" "{currentExe}"
            del /f /q "{currentExe}.old" >nul 2>&1
            del /f /q "%~f0" >nul 2>&1
            """;

        File.WriteAllText(scriptPath, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    public static void VerifySha256(string filePath, string expectedSha256)
    {
        using var stream = File.OpenRead(filePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!hash.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update package failed integrity verification.");
    }

    private static string FormatVer(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    private static string NormalizeVersion(string text)
    {
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
            return $"{parts[0]}.{parts[1]}.{parts[2]}";

        return text;
    }

    private static (string? Url, string? Sha256, string? Name) FindAsset(JsonElement assets, string exactName)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.Equals(exactName, StringComparison.OrdinalIgnoreCase))
                continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            return (url, FindMatchingSha256Asset(assets, name), name);
        }

        return (null, null, null);
    }

    private static (string? Url, string? Sha256, string? Name) FindAssetEndsWith(JsonElement assets, string suffix)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            return (url, FindMatchingSha256Asset(assets, name), name);
        }

        return (null, null, null);
    }

    private static (string? Url, string? Sha256) FindZipAsset(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets))
            return (null, null);

        string? zipUrl = null;
        string? zipName = null;
        string? sha256 = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrEmpty(url))
                continue;

            if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            {
                sha256 ??= TryReadSha256FromAsset(url);
                continue;
            }

            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            {
                zipUrl = url;
                zipName = name;
                break;
            }

            zipUrl ??= url;
            zipName ??= name;
        }

        if (zipName != null && string.IsNullOrEmpty(sha256))
            sha256 = FindMatchingSha256Asset(assets, zipName);

        return (zipUrl, sha256);
    }

    private static string? FindMatchingSha256Asset(JsonElement assets, string fileName)
    {
        var expectedName = fileName + ".sha256";
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                continue;

            var url = asset.GetProperty("browser_download_url").GetString();
            return string.IsNullOrEmpty(url) ? null : TryReadSha256FromAsset(url);
        }

        return null;
    }

    private static string? TryReadSha256FromAsset(string url)
    {
        try
        {
            var text = AppHttp.Client.GetStringAsync(url).GetAwaiter().GetResult().Trim();
            var token = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }
}
