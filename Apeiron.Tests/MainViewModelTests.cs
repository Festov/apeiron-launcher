using Apeiron.Services;
using Apeiron.ViewModels;
using Xunit;

namespace Apeiron.Tests;

public class MainViewModelTests
{
    [Fact]
    public void RefreshPlayState_reflects_install_state()
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
            LocalizationService.Initialize("en");
            viewModel.RefreshPlayState(root);
            Assert.Equal(LocalizationService.T("main.download"), viewModel.PlayButtonText);
            Assert.True(viewModel.PlayButtonEnabled);

            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), """{"mainClass":"net.minecraft.client.main.Main"}""");
            File.WriteAllBytes(Path.Combine(versionDir, $"{versionId}.jar"), new byte[20_000]);

            viewModel.RefreshPlayState(root);
            Assert.Equal(LocalizationService.T("main.play"), viewModel.PlayButtonText);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void RefreshPlayState_uses_build_name_in_status()
    {
        LocalizationService.Initialize("en");
        var viewModel = new MainViewModel
        {
            CurrentBuild = new BuildInfo { Name = "Test Pack", MinecraftVersion = "1.20.1", IsModded = false }
        };

        viewModel.RefreshPlayState(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Contains("Test Pack", viewModel.StatusText);
    }

    [Fact]
    public void SetTransientPlayButton_overrides_until_refresh()
    {
        LocalizationService.Initialize("en");
        var viewModel = new MainViewModel
        {
            CurrentBuild = new BuildInfo { Name = "Pack", MinecraftVersion = "1.20.1", IsModded = false }
        };

        viewModel.SetTransientPlayButton("main.launching", enabled: false);
        Assert.Equal(LocalizationService.T("main.launching"), viewModel.PlayButtonText);
        Assert.False(viewModel.PlayButtonEnabled);

        viewModel.RefreshPlayState(Path.GetTempPath());
        Assert.True(viewModel.PlayButtonEnabled);
    }

    [Fact]
    public void ApplyDownloadProgress_sets_value_and_text()
    {
        var viewModel = new MainViewModel();
        viewModel.ApplyDownloadProgress(42, "Downloading assets");

        Assert.True(viewModel.IsProgressVisible);
        Assert.False(viewModel.IsProgressIndeterminate);
        Assert.Equal(42, viewModel.ProgressValue);
        Assert.Equal("Downloading assets", viewModel.ProgressText);
    }

    [Fact]
    public void ApplyDownloadProgress_indeterminate_when_progress_negative()
    {
        var viewModel = new MainViewModel();
        viewModel.ApplyDownloadProgress(-1, "Installing loader");

        Assert.True(viewModel.IsProgressVisible);
        Assert.True(viewModel.IsProgressIndeterminate);
    }

    [Fact]
    public void BeginDownloadUi_shows_cancel_hides_open_log()
    {
        var viewModel = new MainViewModel();
        viewModel.ShowOpenInstallLog();
        viewModel.BeginDownloadUi();

        Assert.True(viewModel.IsCancelDownloadVisible);
        Assert.False(viewModel.IsOpenInstallLogVisible);
        Assert.True(viewModel.IsProgressVisible);
    }
}
