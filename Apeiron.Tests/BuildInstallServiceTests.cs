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
            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), "{}");

            var build = new BuildInfo { MinecraftVersion = versionId, IsModded = false };

            Assert.False(BuildInstallService.IsInstalled(root, build));

            File.WriteAllBytes(Path.Combine(versionDir, $"{versionId}.jar"), new byte[20_000]);
            Assert.True(BuildInstallService.IsInstalled(root, build));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
