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

public sealed class LauncherUpdateInfo
{
    public Version LatestVersion { get; init; } = new(0, 0);
    public string DownloadUrl { get; init; } = "";
    public string? ExpectedSha256 { get; init; }
    public string ReleasePageUrl { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
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

        var (downloadUrl, sha256) = FindZipAsset(root);
        if (string.IsNullOrEmpty(downloadUrl))
            return null;

        return new LauncherUpdateInfo
        {
            LatestVersion = latestVersion,
            DownloadUrl = downloadUrl,
            ExpectedSha256 = sha256,
            ReleasePageUrl = root.GetProperty("html_url").GetString() ?? "",
            ReleaseNotes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : ""
        };
    }

    public static async Task<string> DownloadUpdatePackageAsync(
        string downloadUrl,
        string? expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), "apeiron-update-" + Guid.NewGuid().ToString("N") + ".zip");
        using var response = await AppHttp.Client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(tempZip);
        await input.CopyToAsync(output, cancellationToken);

        if (!string.IsNullOrWhiteSpace(expectedSha256))
            VerifySha256(tempZip, expectedSha256);

        return tempZip;
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

    private static string NormalizeVersion(string text)
    {
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
            return $"{parts[0]}.{parts[1]}.{parts[2]}";

        return text;
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

    private static string? FindMatchingSha256Asset(JsonElement assets, string zipName)
    {
        var expectedName = zipName + ".sha256";
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
