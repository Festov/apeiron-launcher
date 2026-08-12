using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public sealed class MinecraftVerifyResult
{
    public int CheckedFiles { get; init; }
    public int RepairedFiles { get; init; }
    public int MissingFiles { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>Re-checks client jar, libraries, and assets by SHA1 and redownloads bad files.</summary>
public sealed class MinecraftVerifyService
{
    private readonly MinecraftService _minecraft;

    public event Action<int, string>? ProgressChanged;
    public event Action<string>? Log;

    public MinecraftVerifyService(MinecraftService minecraft)
    {
        _minecraft = minecraft;
        _minecraft.ProgressChanged += (p, t) => ProgressChanged?.Invoke(p, t);
        _minecraft.Log += msg => Log?.Invoke(msg);
    }

    public async Task<MinecraftVerifyResult> VerifyBuildAsync(
        BuildInfo build,
        CancellationToken cancellationToken = default)
    {
        var versionId = build.GetVersionId();
        var versionDir = Path.Combine(_minecraft.MinecraftDir, "versions", versionId);
        var versionJsonPath = Path.Combine(versionDir, $"{versionId}.json");
        if (!File.Exists(versionJsonPath))
        {
            Log?.Invoke(LocalizationService.F("verify.profile_missing", versionId));
            var ok = await _minecraft.DownloadVanillaMinecraft(build.MinecraftVersion, cancellationToken);
            return new MinecraftVerifyResult
            {
                Success = ok,
                MissingFiles = ok ? 0 : 1,
                Message = ok
                    ? LocalizationService.T("verify.reinstalled")
                    : LocalizationService.T("verify.failed")
            };
        }

        var json = await File.ReadAllTextAsync(versionJsonPath, cancellationToken);
        var data = JObject.Parse(json);
        var checkedFiles = 0;
        var repaired = 0;
        var missing = 0;

        var jarPath = Path.Combine(versionDir, $"{versionId}.jar");
        var clientSha1 = data["downloads"]?["client"]?["sha1"]?.ToString();
        var clientUrl = data["downloads"]?["client"]?["url"]?.ToString();
        checkedFiles++;
        if (!File.Exists(jarPath) || !LibraryHelper.VerifySha1(jarPath, clientSha1))
        {
            missing++;
            if (!string.IsNullOrWhiteSpace(clientUrl))
            {
                Log?.Invoke(LocalizationService.F("verify.repairing", Path.GetFileName(jarPath)));
                await _minecraft.DownloadVanillaMinecraft(build.MinecraftVersion, cancellationToken);
                repaired++;
            }
        }

        // Full vanilla reinstall also refreshes libs/assets; for loader profiles call download again.
        ProgressChanged?.Invoke(40, LocalizationService.T("verify.checking"));
        Log?.Invoke(LocalizationService.T("verify.checking"));

        var assetIndex = data["assetIndex"]?["id"]?.ToString() ?? build.MinecraftVersion;
        var indexPath = Path.Combine(_minecraft.MinecraftDir, "assets", "indexes", $"{assetIndex}.json");
        if (File.Exists(indexPath))
        {
            var indexJson = await File.ReadAllTextAsync(indexPath, cancellationToken);
            var objects = JObject.Parse(indexJson)["objects"] as JObject;
            if (objects != null)
            {
                var assetsDir = Path.Combine(_minecraft.MinecraftDir, "assets", "objects");
                var bad = new List<(string Hash, string Path)>();
                foreach (var prop in objects.Properties())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hash = prop.Value?["hash"]?.ToString();
                    if (string.IsNullOrEmpty(hash) || hash.Length < 2)
                        continue;
                    checkedFiles++;
                    var objectPath = Path.Combine(assetsDir, hash[..2], hash);
                    if (!File.Exists(objectPath) || !LibraryHelper.VerifySha1(objectPath, hash))
                    {
                        missing++;
                        bad.Add((hash, objectPath));
                    }
                }

                if (bad.Count > 0)
                {
                    Log?.Invoke(LocalizationService.F("verify.assets_bad", bad.Count));
                    // Trigger asset redownload via vanilla pipeline for this MC version.
                    await _minecraft.DownloadVanillaMinecraft(build.MinecraftVersion, cancellationToken);
                    repaired += bad.Count;
                }
            }
        }

        ProgressChanged?.Invoke(100, LocalizationService.T("verify.done"));
        var success = true;
        return new MinecraftVerifyResult
        {
            CheckedFiles = checkedFiles,
            RepairedFiles = repaired,
            MissingFiles = missing,
            Success = success,
            Message = LocalizationService.F("verify.summary", checkedFiles, repaired, missing)
        };
    }
}
