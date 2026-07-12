using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LaunchArgsParserTests
{
    [Fact]
    public void Parse_reads_launch_flag_with_separate_value()
    {
        var parsed = LaunchArgsParser.Parse(new[] { "--launch", "My Build" });
        Assert.Equal("My Build", parsed.LaunchTarget);
        Assert.True(parsed.HasLaunchTarget);
    }

    [Fact]
    public void Parse_reads_launch_flag_with_equals_syntax()
    {
        var parsed = LaunchArgsParser.Parse(new[] { "--launch=build-id-123" });
        Assert.Equal("build-id-123", parsed.LaunchTarget);
    }

    [Fact]
    public void Parse_returns_empty_when_flag_missing()
    {
        var parsed = LaunchArgsParser.Parse(Array.Empty<string>());
        Assert.False(parsed.HasLaunchTarget);
        Assert.Null(parsed.LaunchTarget);
        Assert.False(parsed.ShowHelp);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void Parse_detects_help_flags(string flag)
    {
        var parsed = LaunchArgsParser.Parse(new[] { flag });
        Assert.True(parsed.ShowHelp);
    }

    [Fact]
    public void Parse_supports_help_and_launch_together()
    {
        var parsed = LaunchArgsParser.Parse(new[] { "--help", "--launch", "Test" });
        Assert.True(parsed.ShowHelp);
        Assert.Equal("Test", parsed.LaunchTarget);
    }
}
