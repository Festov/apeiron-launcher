using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class DownloadProgressHelperTests
{
    [Fact]
    public void CreateUpdate_clamps_progress_to_0_100()
    {
        var update = DownloadProgressHelper.CreateUpdate(150, "Downloading");
        Assert.True(update.UpdateBarValue);
        Assert.Equal(100, update.BarValue);
        Assert.Equal("Downloading", update.StatusText);
    }

    [Fact]
    public void CreateUpdate_skips_bar_update_for_indeterminate_progress()
    {
        var update = DownloadProgressHelper.CreateUpdate(-1, "Installing loader");
        Assert.False(update.UpdateBarValue);
        Assert.Equal("Installing loader", update.StatusText);
    }
}
