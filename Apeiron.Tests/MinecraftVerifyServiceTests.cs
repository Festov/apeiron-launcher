using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class MinecraftVerifyServiceTests
{
    [Fact]
    public async Task VerifyBuildAsync_intact_client_reports_success()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-verify-" + Guid.NewGuid().ToString("N"));
        var versionId = "1.20.1";
        var versionDir = Path.Combine(root, "versions", versionId);

        try
        {
            Directory.CreateDirectory(versionDir);
            var jarPath = Path.Combine(versionDir, $"{versionId}.jar");
            File.WriteAllBytes(jarPath, new byte[] { 1, 2, 3, 4, 5 });
            File.WriteAllText(
                Path.Combine(versionDir, $"{versionId}.json"),
                """{"id":"1.20.1","downloads":{"client":{"sha1":"","url":""}},"assetIndex":{"id":"1.20"}}""");

            var minecraft = new MinecraftService(root);
            var verifier = new MinecraftVerifyService(minecraft);
            var build = new BuildInfo { MinecraftVersion = versionId, IsModded = false };

            var result = await verifier.VerifyBuildAsync(build);

            Assert.True(result.Success);
            Assert.True(result.CheckedFiles >= 1);
            Assert.Equal(0, result.RepairedFiles);
            Assert.Contains("1", result.Message); // summary includes checked count
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
