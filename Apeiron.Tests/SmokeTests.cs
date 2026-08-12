using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

/// <summary>Fast smoke checks that do not require UI Automation.</summary>
public class SmokeTests
{
    [Fact]
    public void Core_services_construct_without_throwing()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new SettingsService(root);
            settings.Load();
            settings.Save();

            var mc = new MinecraftService(Path.Combine(root, ".minecraft"));
            mc.SetAssetParallelism(16);
            _ = new MinecraftVerifyService(mc);
            _ = new LogService(Path.Combine(root, "logs"));
            _ = JvmProfileHelper.MergeWithCustom(JvmProfileHelper.Performance, "-Dtest=1");
            _ = WebView2Helper.GetUserDataFolder(mc.MinecraftDir);
            _ = LaunchArgsParser.Parse(new[] { "--launch" });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Embedded_skinview_bundle_resource_exists()
    {
        var stream = typeof(SkinService).Assembly
            .GetManifestResourceStream("Apeiron.Assets.skinview3d.bundle.js");
        Assert.NotNull(stream);
        Assert.True(stream!.Length > 1000);
    }
}
