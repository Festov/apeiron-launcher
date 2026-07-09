using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class JavaVersionHelperTests
{
    [Theory]
    [InlineData("26.1", 25)]
    [InlineData("26.2", 25)]
    [InlineData("1.21.4", 21)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.20.1", 17)]
    [InlineData("1.20", 17)]
    [InlineData("1.18", 17)]
    [InlineData("1.17.1", 17)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.15.2", 8)]
    [InlineData("1.12.2", 8)]
    [InlineData("1.8.9", 8)]
    public void GetRequiredJavaMajor_matches_minecraft_version(string mcVersion, int expected) =>
        Assert.Equal(expected, JavaVersionHelper.GetRequiredJavaMajor(mcVersion));

    [Theory]
    [InlineData("1.8.9", 8)]
    [InlineData("1.12.2", 8)]
    [InlineData("1.15.2", 8)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.17.1", 17)]
    [InlineData("1.18", 21)]
    [InlineData("1.20.4", 21)]
    [InlineData("1.21.4", 21)]
    [InlineData("26.1", 25)]
    public void GetMaxJavaMajor_caps_compatible_java(string mcVersion, int expected) =>
        Assert.Equal(expected, JavaVersionHelper.GetMaxJavaMajor(mcVersion));

    [Theory]
    [InlineData("1.15.2", 8)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.17.1", 17)]
    [InlineData("1.18", 21)]
    [InlineData("1.20.4", 21)]
    [InlineData("1.21.4", 21)]
    [InlineData("26.1", 25)]
    public void GetPreferredJavaMajor_installs_correct_oracle_jdk(string mcVersion, int expected) =>
        Assert.Equal(expected, JavaVersionHelper.GetPreferredJavaMajor(mcVersion));

    [Fact]
    public void GetMinecraftJavaMappings_covers_all_ranges()
    {
        var mappings = JavaVersionHelper.GetMinecraftJavaMappings();
        Assert.Equal(5, mappings.Count);
        Assert.Contains(mappings, m => m.MinJava == 8 && m.MaxJava == 8);
        Assert.Contains(mappings, m => m.MinJava == 25);
    }
}
