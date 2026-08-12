using Apeiron.Services;
using System.IO.Compression;
using Xunit;

namespace Apeiron.Tests;

public class OfflineSkinHelperTests
{
    [Fact]
    public void ApplyForOfflineLaunch_writes_resource_pack_and_options()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-offline-skin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new SettingsService(root)
            {
                SkinPreset = "steve",
                SkinModel = "classic",
                OfflineUsername = "TestPlayer"
            };
            settings.Save();

            var mc = Path.Combine(root, ".minecraft");
            var skins = new SkinService(mc);
            var build = new BuildInfo
            {
                Id = "inst-1",
                Name = "Test",
                MinecraftVersion = "1.21",
                InstancePath = Path.Combine(root, "instances", "inst-1")
            };
            build.EnsureInstanceFolders();

            // Ensure steve skin is materialized from embedded resources
            Assert.NotNull(skins.GetStandardSkinPath("steve"));

            OfflineSkinHelper.ApplyForOfflineLaunch(build, settings, skins);

            var pack = Path.Combine(build.GetGameDir(), "resourcepacks", OfflineSkinHelper.ResourcePackFileName);
            Assert.True(File.Exists(pack));
            using (var zip = ZipFile.OpenRead(pack))
                Assert.NotNull(zip.GetEntry("pack.mcmeta"));

            var options = File.ReadAllText(Path.Combine(build.GetGameDir(), "options.txt"));
            Assert.Contains("Apeiron-Offline-Skin", options);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
