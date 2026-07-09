using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
        var versionId = build.GetVersionId();
        var jsonPath = Path.Combine(minecraftDir, "versions", versionId, $"{versionId}.json");
        var jarPath = Path.Combine(minecraftDir, "versions", versionId, $"{versionId}.jar");
        return File.Exists(jsonPath) && IsValidVersionJar(jarPath);
    }

    public static bool IsValidVersionJar(string jarPath) =>
        File.Exists(jarPath) && new FileInfo(jarPath).Length > 10_000;

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
            }

            await _fabricApi.InstallAsync(build, cancellationToken);
        }

        ProgressChanged?.Invoke(100, LocalizationService.T("progress.install.ready"));
        Log?.Invoke(LocalizationService.F("log.build.ready", build.Name));
        return true;
    }
}
