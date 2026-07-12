using Apeiron.Services;
using Apeiron.ViewModels;
using Xunit;

namespace Apeiron.Tests;

public class MainViewModelTests
{
    [Fact]
    public void GetPlayButton_reflects_install_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-vm-" + Guid.NewGuid().ToString("N"));
        var versionId = "1.20.1";
        var versionDir = Path.Combine(root, "versions", versionId);
        var viewModel = new MainViewModel
        {
            CurrentBuild = new BuildInfo { MinecraftVersion = versionId, IsModded = false }
        };

        try
        {
            var download = viewModel.GetPlayButton(root);
            Assert.Equal("⬇️", download.Icon);
            Assert.False(download.IsEnabled == false && viewModel.CurrentBuild != null);

            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), """{"mainClass":"net.minecraft.client.main.Main"}""");
            File.WriteAllBytes(Path.Combine(versionDir, $"{versionId}.jar"), new byte[20_000]);

            var play = viewModel.GetPlayButton(root);
            Assert.Equal("▶", play.Icon);
            Assert.Equal("main.play", play.LocalizationKey);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void GetStatusText_uses_build_name_when_selected()
    {
        LocalizationService.Initialize("en");
        var viewModel = new MainViewModel
        {
            CurrentBuild = new BuildInfo { Name = "Test Pack", MinecraftVersion = "1.20.1", IsModded = false }
        };

        var status = viewModel.GetStatusText(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Contains("Test Pack", status);
    }
}
