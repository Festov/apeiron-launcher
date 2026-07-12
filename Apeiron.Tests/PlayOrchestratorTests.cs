using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class PlayOrchestratorTests
{
    [Fact]
    public async Task InstallIfNeededAsync_skips_when_build_already_installed()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-play-" + Guid.NewGuid().ToString("N"));
        var versionId = "1.20.1";
        var versionDir = Path.Combine(root, "versions", versionId);

        try
        {
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), """{"mainClass":"net.minecraft.client.main.Main"}""");
            File.WriteAllBytes(Path.Combine(versionDir, $"{versionId}.jar"), new byte[20_000]);

            var minecraft = new MinecraftService(root);
            var loader = new LoaderService(root);
            var orchestrator = new PlayOrchestrator(
                minecraft,
                new BuildInstallService(minecraft, loader),
                new VersionLauncher(root),
                new JavaService());

            var build = new BuildInfo { MinecraftVersion = versionId, IsModded = false };
            var result = await orchestrator.InstallIfNeededAsync(build, CancellationToken.None);

            Assert.Equal(InstallFlowResult.AlreadyInstalled, result);
            Assert.True(orchestrator.IsInstalled(build));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ResolveJavaPath_delegates_to_java_service()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-play-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var minecraft = new MinecraftService(root);
            var loader = new LoaderService(root);
            var java = new JavaService();
            var orchestrator = new PlayOrchestrator(
                minecraft,
                new BuildInstallService(minecraft, loader),
                new VersionLauncher(root),
                java);

            var build = new BuildInfo { MinecraftVersion = "1.20.1", IsModded = false };
            Assert.Equal(java.ResolveJavaPath(build.MinecraftVersion), orchestrator.ResolveJavaPath(build));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
