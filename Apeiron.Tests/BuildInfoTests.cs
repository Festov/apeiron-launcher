using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class BuildInfoTests
{
    [Fact]
    public void GetVersionId_returns_vanilla_id_for_unmodded_build() =>
        Assert.Equal("1.20.1", new BuildInfo { MinecraftVersion = "1.20.1", IsModded = false }.GetVersionId());

    [Theory]
    [InlineData("fabric", "0.15.0", "1.20.1", "fabric-loader-0.15.0-1.20.1")]
    [InlineData("quilt", "0.24.0", "1.21", "quilt-loader-0.24.0-1.21")]
    [InlineData("forge", "47.3.0", "1.20.1", "1.20.1-forge-47.3.0")]
    [InlineData("neoforge", "21.1.0", "1.21.1", "neoforge-21.1.0")]
    public void GetVersionId_formats_modded_profiles(string loader, string loaderVersion, string mc, string expected)
    {
        var build = new BuildInfo
        {
            MinecraftVersion = mc,
            Loader = loader,
            LoaderVersion = loaderVersion,
            IsModded = true
        };

        Assert.Equal(expected, build.GetVersionId());
    }

    [Theory]
    [InlineData(false, "", true)]
    [InlineData(true, "fabric", true)]
    [InlineData(true, "quilt", true)]
    [InlineData(true, "forge", true)]
    [InlineData(true, "neoforge", true)]
    [InlineData(true, "optifine", false)]
    public void IsLoaderSupported_accepts_known_loaders(bool isModded, string loader, bool expected)
    {
        var build = new BuildInfo { IsModded = isModded, Loader = loader };
        Assert.Equal(expected, build.IsLoaderSupported());
    }

    [Theory]
    [InlineData("1.20.1", "", "", false, "1.20.1")]
    [InlineData("1.20.1", "Fabric", "0.15.0", true, "1.20.1 - Fabric 0.15.0")]
    [InlineData("1.21", "Forge", "", true, "1.21 - Forge")]
    public void GenerateDefaultName_formats_display_name(string mc, string loader, string loaderVersion, bool isModded, string expected) =>
        Assert.Equal(expected, BuildInfo.GenerateDefaultName(mc, loader, loaderVersion, isModded));

    [Theory]
    [InlineData("fabric", true, "🧵")]
    [InlineData("forge", true, "🔨")]
    [InlineData("", false, "🟩")]
    public void GetLoaderIcon_returns_expected_glyph(string loader, bool isModded, string expected) =>
        Assert.Equal(expected, BuildInfo.GetLoaderIcon(loader, isModded));

    [Fact]
    public void ResolveRamGb_uses_global_when_instance_value_missing()
    {
        var build = new BuildInfo { RamGb = 0 };
        Assert.Equal(8, build.ResolveRamGb(8));
        Assert.Equal(12, new BuildInfo { RamGb = 12 }.ResolveRamGb(8));
    }
}
