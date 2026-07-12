using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Apeiron.Services;

public enum InstallFlowResult
{
    AlreadyInstalled,
    Success,
    Failed,
    Cancelled
}

public sealed class PlayOrchestrator
{
    private readonly MinecraftService _minecraft;
    private readonly BuildInstallService _buildInstall;
    private readonly VersionLauncher _versionLauncher;
    private readonly JavaService _java;

    public PlayOrchestrator(
        MinecraftService minecraft,
        BuildInstallService buildInstall,
        VersionLauncher versionLauncher,
        JavaService java)
    {
        _minecraft = minecraft;
        _buildInstall = buildInstall;
        _versionLauncher = versionLauncher;
        _java = java;
    }

    public bool IsInstalled(BuildInfo build) =>
        BuildInstallService.IsInstalled(_minecraft.MinecraftDir, build);

    public async Task<InstallFlowResult> InstallIfNeededAsync(BuildInfo build, CancellationToken cancellationToken)
    {
        if (IsInstalled(build))
            return InstallFlowResult.AlreadyInstalled;

        var success = await _buildInstall.InstallAsync(build, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return InstallFlowResult.Cancelled;

        return success ? InstallFlowResult.Success : InstallFlowResult.Failed;
    }

    public string? ResolveJavaPath(BuildInfo build) =>
        _java.ResolveJavaPath(build.MinecraftVersion);

    public async Task<Process?> LaunchGameAsync(BuildInfo build, LaunchIdentity identity, int globalRamGb)
    {
        var javaPath = ResolveJavaPath(build);
        if (string.IsNullOrEmpty(javaPath))
            return null;

        var ram = build.ResolveRamGb(globalRamGb);
        return await _versionLauncher.LaunchAsync(
            build,
            identity.Username,
            identity.Uuid,
            identity.AccessToken,
            ram,
            javaPath);
    }
}
