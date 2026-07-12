using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Apeiron.Services;

public enum PlayValidationResult
{
    Ok,
    NoBuild,
    UnsupportedLoader
}

public enum LaunchPreparationResult
{
    Ready,
    JavaMissing,
    SessionExpired
}

public readonly record struct LaunchPreparation(LaunchPreparationResult Result, LaunchIdentity Identity);

public sealed class LauncherOrchestrator
{
    private readonly PlayOrchestrator _play;
    private readonly MinecraftService _minecraft;

    public LauncherOrchestrator(PlayOrchestrator play, MinecraftService minecraft)
    {
        _play = play;
        _minecraft = minecraft;
    }

    public PlayValidationResult ValidateBuild(BuildInfo? build)
    {
        if (build == null)
            return PlayValidationResult.NoBuild;

        if (!build.IsLoaderSupported())
            return PlayValidationResult.UnsupportedLoader;

        return PlayValidationResult.Ok;
    }

    public bool IsInstalled(BuildInfo build) => _play.IsInstalled(build);

    public IReadOnlyList<string> ClearForReinstall(BuildInfo build) =>
        BuildInstallService.ClearInstalledArtifacts(_minecraft.MinecraftDir, build);

    public Task<InstallFlowResult> InstallIfNeededAsync(BuildInfo build, CancellationToken cancellationToken) =>
        _play.InstallIfNeededAsync(build, cancellationToken);

    public async Task<InstallFlowResult> ReinstallAsync(BuildInfo build, CancellationToken cancellationToken)
    {
        ClearForReinstall(build);
        return await _play.InstallIfNeededAsync(build, cancellationToken);
    }

    public string? ResolveJavaPath(BuildInfo build) => _play.ResolveJavaPath(build);

    public async Task<LaunchPreparation> PrepareLaunchAsync(
        BuildInfo build,
        AuthService auth,
        SettingsService settings,
        string offlineText)
    {
        if (string.IsNullOrEmpty(_play.ResolveJavaPath(build)))
            return new LaunchPreparation(LaunchPreparationResult.JavaMissing, default);

        var resolution = await LaunchCoordinator.ResolveLaunchIdentityAsync(auth, settings, offlineText);
        if (resolution.Status == LaunchIdentityStatus.SessionExpired)
            return new LaunchPreparation(LaunchPreparationResult.SessionExpired, default);

        return new LaunchPreparation(LaunchPreparationResult.Ready, resolution.Identity);
    }

    public Task<Process?> LaunchGameAsync(BuildInfo build, LaunchIdentity identity, int globalRamGb) =>
        _play.LaunchGameAsync(build, identity, globalRamGb);
}
