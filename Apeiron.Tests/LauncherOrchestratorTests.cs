using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LauncherOrchestratorTests
{
    [Fact]
    public void ValidateBuild_detects_missing_and_unsupported_builds()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-launcher-orch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var minecraft = new MinecraftService(root);
            var orchestrator = new LauncherOrchestrator(
                new PlayOrchestrator(minecraft, new BuildInstallService(minecraft, new LoaderService(root)), new VersionLauncher(root), new JavaService()),
                minecraft);

            Assert.Equal(PlayValidationResult.NoBuild, orchestrator.ValidateBuild(null));

            var unsupported = new BuildInfo { Loader = "unknown", IsModded = true };
            Assert.Equal(PlayValidationResult.UnsupportedLoader, orchestrator.ValidateBuild(unsupported));

            var vanilla = new BuildInfo { MinecraftVersion = "1.20.1", IsModded = false };
            Assert.Equal(PlayValidationResult.Ok, orchestrator.ValidateBuild(vanilla));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task ReinstallAsync_clears_artifacts_before_install()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-launcher-orch-" + Guid.NewGuid().ToString("N"));
        var build = new BuildInfo
        {
            MinecraftVersion = "1.20.1",
            Loader = "Fabric",
            LoaderVersion = "0.15.0",
            IsModded = true
        };

        try
        {
            var versionId = build.GetVersionId();
            Directory.CreateDirectory(Path.Combine(root, "versions", versionId));
            Directory.CreateDirectory(Path.Combine(root, "versions", build.MinecraftVersion));

            var minecraft = new MinecraftService(root);
            var orchestrator = new LauncherOrchestrator(
                new PlayOrchestrator(minecraft, new BuildInstallService(minecraft, new LoaderService(root)), new VersionLauncher(root), new JavaService()),
                minecraft);

            var removed = orchestrator.ClearForReinstall(build);
            Assert.Contains(versionId, removed);
            Assert.False(Directory.Exists(Path.Combine(root, "versions", versionId)));

            var result = await orchestrator.ReinstallAsync(build, new CancellationToken(canceled: true));
            Assert.Equal(InstallFlowResult.Cancelled, result);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
