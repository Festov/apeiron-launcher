using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LauncherUpdatePromptHelperTests
{
    [Fact]
    public void BuildPrompt_includes_release_notes_when_present()
    {
        LocalizationService.Initialize("en");
        var update = new LauncherUpdateInfo
        {
            LatestVersion = new Version(1, 5, 0),
            ReleaseNotes = "- Added drag-and-drop\n- Fixed launch"
        };

        var prompt = LauncherUpdatePromptHelper.BuildPrompt(update, new Version(1, 4, 0));

        Assert.Contains("1.5.0", prompt);
        Assert.Contains("drag-and-drop", prompt);
    }

    [Fact]
    public void BuildPrompt_omits_notes_when_empty()
    {
        LocalizationService.Initialize("en");
        var update = new LauncherUpdateInfo
        {
            LatestVersion = new Version(1, 5, 0),
            ReleaseNotes = ""
        };

        var prompt = LauncherUpdatePromptHelper.BuildPrompt(update, new Version(1, 4, 0));

        Assert.DoesNotContain("drag-and-drop", prompt);
        Assert.Contains("1.5.0", prompt);
    }
}
