using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class BuildInstallServiceTests
{
    [Fact]
    public void IsInstalled_requires_both_json_and_jar()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-test-" + Guid.NewGuid().ToString("N"));
        var versionId = "1.20.1";
        var versionDir = Path.Combine(root, "versions", versionId);

        try
        {
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), """{"mainClass":"net.minecraft.client.main.Main"}""");

            var build = new BuildInfo { MinecraftVersion = versionId, IsModded = false };

            Assert.False(BuildInstallService.IsInstalled(root, build));

            File.WriteAllBytes(Path.Combine(versionDir, $"{versionId}.jar"), new byte[20_000]);
            Assert.True(BuildInstallService.IsInstalled(root, build));

            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), "{}");
            Assert.False(BuildInstallService.IsInstalled(root, build));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ClearInstalledArtifacts_removes_version_and_base_folders_for_modded_build()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-test-" + Guid.NewGuid().ToString("N"));
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

            var removed = BuildInstallService.ClearInstalledArtifacts(root, build);

            Assert.Contains(versionId, removed);
            Assert.Contains(build.MinecraftVersion, removed);
            Assert.False(Directory.Exists(Path.Combine(root, "versions", versionId)));
            Assert.False(Directory.Exists(Path.Combine(root, "versions", build.MinecraftVersion)));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
