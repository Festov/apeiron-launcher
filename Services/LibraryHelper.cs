using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public static class LibraryHelper
{
    public static string GetJarPath(string mavenName, string librariesDir)
    {
        var parts = mavenName.Split(':');
        if (parts.Length < 3)
            return "";

        var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";

        return Path.Combine(librariesDir, group, artifact, version, $"{artifact}-{version}{classifier}.jar");
    }

    public static bool VerifySha1(string path, string? expectedSha1)
    {
        if (string.IsNullOrWhiteSpace(expectedSha1) || !File.Exists(path))
            return true;

        try
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(path);
            var hash = sha1.ComputeHash(stream);
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            return hex == expectedSha1.ToLowerInvariant();
        }
        catch
        {
            return false;
        }
    }

    public static async Task DownloadFromVersionLibraryAsync(
        JToken lib,
        string librariesDir,
        Func<string, string, string?, CancellationToken, Task> downloadFile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var libName = lib["name"]?.ToString();
        if (string.IsNullOrEmpty(libName))
            return;

        var downloads = lib["downloads"] as JObject;
        if (downloads != null)
        {
            var artifact = downloads["artifact"];
            if (artifact != null)
            {
                var url = artifact["url"]?.ToString();
                var sha1 = artifact["sha1"]?.ToString();
                if (!string.IsNullOrEmpty(url))
                {
                    var path = GetJarPath(libName, librariesDir);
                    await DownloadIfNeededAsync(path, url, sha1, downloadFile, cancellationToken);
                }
            }

            var classifiers = downloads["classifiers"] as JObject;
            if (classifiers != null)
            {
                foreach (var classifier in classifiers.Properties())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = classifier.Value as JObject;
                    var url = entry?["url"]?.ToString();
                    var sha1 = entry?["sha1"]?.ToString();
                    if (string.IsNullOrEmpty(url))
                        continue;

                    var classifiedName = $"{libName}:{classifier.Name}";
                    var path = GetJarPath(classifiedName, librariesDir);
                    await DownloadIfNeededAsync(path, url, sha1, downloadFile, cancellationToken);
                }
            }

            return;
        }

        var legacyBaseUrl = lib["url"]?.ToString();
        var legacySha1 = lib["sha1"]?.ToString();
        var legacyUrl = BuildMavenJarUrl(libName, legacyBaseUrl);
        if (!string.IsNullOrEmpty(legacyUrl))
        {
            var path = GetJarPath(libName, librariesDir);
            await DownloadIfNeededAsync(path, legacyUrl, legacySha1, downloadFile, cancellationToken);
        }
    }

    public static string? BuildMavenJarUrl(string mavenName, string? baseRepoUrl)
    {
        if (string.IsNullOrWhiteSpace(baseRepoUrl))
            return null;

        var parts = mavenName.Split(':');
        if (parts.Length < 3)
            return null;

        var groupPath = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";
        var fileName = $"{artifact}-{version}{classifier}.jar";

        return $"{baseRepoUrl.TrimEnd('/')}/{groupPath}/{artifact}/{version}/{fileName}";
    }

    private static async Task DownloadIfNeededAsync(
        string path,
        string url,
        string? sha1,
        Func<string, string, string?, CancellationToken, Task> downloadFile,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path) && VerifySha1(path, sha1))
            return;

        if (File.Exists(path))
            File.Delete(path);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await downloadFile(url, path, sha1, cancellationToken);
    }
}
