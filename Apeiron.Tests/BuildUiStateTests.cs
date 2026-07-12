using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class BuildUiStateTests
{
    [Fact]
    public void GetPlayButtonMode_reflects_install_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-ui-" + Guid.NewGuid().ToString("N"));
        var versionId = "1.20.1";
        var versionDir = Path.Combine(root, "versions", versionId);
        var build = new BuildInfo { MinecraftVersion = versionId, IsModded = false };

        try
        {
            Assert.Equal(PlayButtonMode.NoBuilds, BuildUiState.GetPlayButtonMode(null, root));
            Assert.Equal(PlayButtonMode.Download, BuildUiState.GetPlayButtonMode(build, root));

            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), """{"mainClass":"net.minecraft.client.main.Main"}""");
            File.WriteAllBytes(Path.Combine(versionDir, $"{versionId}.jar"), new byte[20_000]);

            Assert.Equal(PlayButtonMode.Play, BuildUiState.GetPlayButtonMode(build, root));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Theory]
    [InlineData(PlayButtonMode.Play, "▶", "main.play")]
    [InlineData(PlayButtonMode.Download, "⬇️", "main.download")]
    public void GetPlayButtonContent_maps_mode_to_icon_and_key(PlayButtonMode mode, string icon, string key)
    {
        var content = BuildUiState.GetPlayButtonContent(mode);
        Assert.Equal(icon, content.Icon);
        Assert.Equal(key, content.LocalizationKey);
    }

    [Fact]
    public void GetPlayButtonPresentation_marks_null_build_disabled()
    {
        var presentation = BuildUiState.GetPlayButtonPresentation(null, "C:\\test");
        Assert.False(presentation.IsEnabled);
        Assert.Equal("main.no_builds_short", presentation.LocalizationKey);
    }
}
