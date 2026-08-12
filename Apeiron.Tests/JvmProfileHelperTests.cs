using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class JvmProfileHelperTests
{
    [Fact]
    public void Profiles_contains_default_and_known_ids()
    {
        Assert.Contains(JvmProfileHelper.Profiles, p => p.Id == JvmProfileHelper.Default);
        Assert.Contains(JvmProfileHelper.Profiles, p => p.Id == JvmProfileHelper.Performance);
        Assert.Contains(JvmProfileHelper.Profiles, p => p.Id == JvmProfileHelper.LowLatency);
        Assert.Contains(JvmProfileHelper.Profiles, p => p.Id == JvmProfileHelper.Debug);
    }

    [Fact]
    public void GetArgs_default_is_empty()
    {
        Assert.Equal("", JvmProfileHelper.GetArgs(JvmProfileHelper.Default));
        Assert.Equal("", JvmProfileHelper.GetArgs(null));
        Assert.Equal("", JvmProfileHelper.GetArgs("unknown"));
    }

    [Fact]
    public void GetArgs_performance_includes_g1()
    {
        var args = JvmProfileHelper.GetArgs(JvmProfileHelper.Performance);
        Assert.Contains("UseG1GC", args);
        Assert.Contains("AlwaysPreTouch", args);
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("default", "-XX:+UseG1GC", "-XX:+UseG1GC")]
    [InlineData("performance", "", "contains-profile")]
    [InlineData("performance", "-Dcustom=1", "merged")]
    public void MergeWithCustom_combines_profile_and_custom(string? profile, string? custom, string expectation)
    {
        var merged = JvmProfileHelper.MergeWithCustom(profile, custom);
        switch (expectation)
        {
            case "":
                Assert.Equal("", merged);
                break;
            case "-XX:+UseG1GC":
                Assert.Equal("-XX:+UseG1GC", merged);
                break;
            case "contains-profile":
                Assert.Contains("UseG1GC", merged);
                Assert.DoesNotContain("-Dcustom", merged);
                break;
            case "merged":
                Assert.Contains("UseG1GC", merged);
                Assert.Contains("-Dcustom=1", merged);
                break;
        }
    }
}
