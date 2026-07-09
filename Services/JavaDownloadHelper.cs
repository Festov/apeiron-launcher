using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Apeiron.Services;

public static class JavaDownloadHelper
{
    public static Task<IReadOnlyList<string>> GetInstallerDownloadUrlsAsync(int javaMajor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(GetOracleInstallerUrls(javaMajor).ToList());
    }

    public static async Task<bool> DownloadJdkInstallerAsync(
        int javaMajor,
        string destinationPath,
        Action<int, string>? progressChanged = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath))
            File.Delete(destinationPath);

        var urls = await GetInstallerDownloadUrlsAsync(javaMajor, cancellationToken);
        var attempt = 0;

        foreach (var url in urls)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                log?.Invoke(LocalizationService.F("log.java.trying_mirror", attempt, urls.Count));
                await DownloadFileAsync(url, destinationPath, javaMajor, progressChanged, cancellationToken);

                if (!IsValidInstallerPackage(destinationPath))
                {
                    log?.Invoke(LocalizationService.T("log.java.invalid_installer"));
                    File.Delete(destinationPath);
                    continue;
                }

                return true;
            }
            catch (Exception ex)
            {
                if (File.Exists(destinationPath))
                {
                    try { File.Delete(destinationPath); } catch { }
                }

                var details = ex.InnerException?.Message ?? ex.Message;
                log?.Invoke(LocalizationService.F("log.java.mirror_failed", attempt, details));
            }
        }

        return false;
    }

    public static bool IsValidInstallerPackage(string path) =>
        JavaInstallHelper.IsValidOracleExe(path) || JavaInstallHelper.IsValidOracleZip(path);

    public static string? FindJdkRoot(string extractDir)
    {
        if (File.Exists(Path.Combine(extractDir, "bin", "java.exe")))
            return extractDir;

        foreach (var subDir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(subDir, "bin", "java.exe")))
                return subDir;
        }

        return null;
    }

    private static IEnumerable<string> GetOracleInstallerUrls(int javaMajor)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (javaMajor == 8)
        {
            foreach (var url in GetOracleJava8Urls())
            {
                if (seen.Add(url))
                    yield return url;
            }

            yield break;
        }

        foreach (var file in GetOracleFilenames(javaMajor))
        {
            string url;
            if (file.StartsWith("latest:", StringComparison.Ordinal))
                url = $"https://download.oracle.com/java/{javaMajor}/latest/{file["latest:".Length..]}";
            else
                url = $"https://download.oracle.com/java/{javaMajor}/archive/{file}";

            if (seen.Add(url))
                yield return url;
        }
    }

  // Java 8 is not on download.oracle.com/java/8/archive/ — only OTN paths or Adobe ColdFusion mirror (same Oracle JDK).
    private static IEnumerable<string> GetOracleJava8Urls()
    {
        // Latest Adobe ColdFusion mirror (official Oracle JDK installer, no login).
        yield return "https://cfdownload.adobe.com/pub/adobe/coldfusion/java/java8/java8u491/jdk/jdk-8u491-windows-x64.exe";
        yield return "https://cfdownload.adobe.com/pub/adobe/coldfusion/java/java8/java8u491/jdk/jdk-8u491-windows-x64.zip";

        foreach (var (versionTag, hash, fileName) in OracleJava8OtnEntries)
        {
            yield return $"https://download.oracle.com/otn-pub/java/jdk/{versionTag}/{hash}/{fileName}";
        }

        foreach (var update in new[] { 481, 471, 461, 451, 441, 431, 421, 401, 391 })
        {
            yield return $"https://cfdownload.adobe.com/pub/adobe/coldfusion/java/java8/java8u{update}/jdk/jdk-8u{update}-windows-x64.exe";
            yield return $"https://cfdownload.adobe.com/pub/adobe/coldfusion/java/java8/java8u{update}/jdk/jdk-8u{update}-windows-x64.zip";
        }
    }

    private static readonly (string VersionTag, string Hash, string FileName)[] OracleJava8OtnEntries =
    [
        ("8u461-b11", "68ce765258164726922591683c51982c", "jdk-8u461-windows-x64.exe"),
        ("8u461-b11", "68ce765258164726922591683c51982c", "jdk-8u461-windows-x64.zip"),
        ("8u381-b09", "8c876547113c4e4aab3c868e9e0ec572", "jdk-8u381-windows-x64.exe"),
        ("8u271-b09", "61ae65e088624f5aaa0b1d2d801acb16", "jdk-8u271-windows-x64.exe"),
        ("8u261-b12", "a4634525489241b9a9e1aa73d9e118e6", "jdk-8u261-windows-x64.exe"),
        ("8u251-b08", "3d5a2bb8f8d4428bbe94aed7ec7ae784", "jdk-8u251-windows-x64.exe"),
    ];

    private static IEnumerable<string> GetOracleFilenames(int javaMajor) => javaMajor switch
    {
        11 =>
        [
            "jdk-11.0.31_windows-x64_bin.exe",
            "jdk-11.0.31_windows-x64_bin.zip",
            "jdk-11.0.24_windows-x64_bin.exe",
            "jdk-11.0.21_windows-x64_bin.exe"
        ],
        17 =>
        [
            "jdk-17.0.19_windows-x64_bin.exe",
            "jdk-17.0.19_windows-x64_bin.zip",
            "jdk-17.0.14_windows-x64_bin.exe",
            "jdk-17.0.12_windows-x64_bin.exe"
        ],
        21 =>
        [
            "latest:jdk-21_windows-x64_bin.exe",
            "jdk-21.0.7_windows-x64_bin.exe",
            "jdk-21.0.7_windows-x64_bin.zip",
            "jdk-21.0.6_windows-x64_bin.exe"
        ],
        25 =>
        [
            "latest:jdk-25_windows-x64_bin.exe",
            "jdk-25.0.1_windows-x64_bin.exe",
            "jdk-25.0.1_windows-x64_bin.zip"
        ],
        _ => []
    };

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        int javaMajor,
        Action<int, string>? progressChanged,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (url.Contains("download.oracle.com", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("Cookie", "gpw_e24=http%3A%2F%2Fwww.oracle.com%2F; oraclelicense=accept-securebackup-cookie");
            request.Headers.TryAddWithoutValidation("Referer", "https://www.oracle.com/java/technologies/downloads/");
        }

        using var response = await AppHttp.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var downloaded = 0L;
        var buffer = new byte[8192];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destinationPath);

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;

            if (total > 0)
            {
                var progress = (int)(downloaded * 100 / total);
                progressChanged?.Invoke(progress, LocalizationService.F("log.java.downloading_progress", javaMajor));
            }
        }
    }
}
