using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public class BuildInstallService
{
    private readonly MinecraftService _minecraft;
    private readonly LoaderService _loader;
    private readonly FabricApiService _fabricApi = new();

    public event Action<string>? Log;
    public event Action<int, string>? ProgressChanged;

    public BuildInstallService(MinecraftService minecraft, LoaderService loader)
    {
        _minecraft = minecraft;
        _loader = loader;

        _minecraft.ProgressChanged += (p, t) => ProgressChanged?.Invoke(p, t);
        _loader.Log += msg => Log?.Invoke(msg);
        _loader.ProgressChanged += (p, t) => ProgressChanged?.Invoke(p, t);
        _fabricApi.Log += msg => Log?.Invoke(msg);
    }

    public static bool IsInstalled(string minecraftDir, BuildInfo build)
    {
        if (build.NeedsModpackContentInstall)
            return false;

        if (string.IsNullOrWhiteSpace(build.MinecraftVersion))
            return false;

        var versionId = build.GetVersionId();
        var versionDir = Path.Combine(minecraftDir, "versions", versionId);
        var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
        var jarPath = Path.Combine(versionDir, $"{versionId}.jar");

        if (!File.Exists(jsonPath) || !IsValidVersionJar(jarPath))
            return false;

        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("mainClass", out var mainClass) ||
                string.IsNullOrWhiteSpace(mainClass.GetString()))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool IsValidVersionJar(string jarPath) =>
        File.Exists(jarPath) && new FileInfo(jarPath).Length > 10_000;

    /// <summary>Removes downloaded version folders so a build can be reinstalled.</summary>
    public static IReadOnlyList<string> ClearInstalledArtifacts(string minecraftDir, BuildInfo build)
    {
        var removed = new List<string>();
        var versionId = build.GetVersionId();
        var versionDir = Path.Combine(minecraftDir, "versions", versionId);
        if (Directory.Exists(versionDir))
        {
            Directory.Delete(versionDir, recursive: true);
            removed.Add(versionId);
        }

        if (build.IsModded)
        {
            var mcDir = Path.Combine(minecraftDir, "versions", build.MinecraftVersion);
            if (Directory.Exists(mcDir))
            {
                Directory.Delete(mcDir, recursive: true);
                removed.Add(build.MinecraftVersion);
            }
        }

        return removed;
    }

    public async Task<bool> InstallAsync(BuildInfo build, CancellationToken cancellationToken = default)
    {
        if (!build.IsLoaderSupported())
            return false;

        build.EnsureInstanceFolders();

        Log?.Invoke(LocalizationService.F("log.build.installing", build.Name));
        ProgressChanged?.Invoke(0, LocalizationService.F("progress.install.mc", build.MinecraftVersion));

        var vanillaOk = await _minecraft.DownloadVanillaMinecraft(build.MinecraftVersion, cancellationToken);
        if (!vanillaOk)
            return false;

        if (build.IsModded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProgressChanged?.Invoke(-1, LocalizationService.F("progress.install.loader", build.Loader, build.LoaderVersion));
            var loaderOk = await _loader.InstallLoader(build, cancellationToken);
            if (!loaderOk)
                return false;

            if (build.InstallFabricApi)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProgressChanged?.Invoke(-1, LocalizationService.T("progress.install.fabric_api"));
                var fabricOk = await _fabricApi.InstallAsync(build, cancellationToken);
                if (!fabricOk)
                    return false;
            }
        }

        ProgressChanged?.Invoke(100, LocalizationService.T("progress.install.ready"));
        Log?.Invoke(LocalizationService.F("log.build.ready", build.Name));
        return true;
    }
}
