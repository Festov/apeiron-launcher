using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class McVersionHelperTests
{
    [Theory]
    [InlineData("1.20.1", "release", true)]
    [InlineData("24w14a", "snapshot", false)]
    [InlineData("1.20.1-pre1", "release", false)]
    public void IsStableRelease_filters_experimental_versions(string id, string type, bool expected) =>
        Assert.Equal(expected, McVersionHelper.IsStableRelease(id, type));

    [Fact]
    public void CompareVersions_orders_newer_versions_first() =>
        Assert.True(McVersionHelper.CompareVersions("1.21", "1.20.4") > 0);

    [Theory]
    [InlineData("1.20", "1.20.1", true)]
    [InlineData("1.20.1", "1.20.1", true)]
    [InlineData("24w14a", "1.20", false)]
    [InlineData("", "1.20.1", true)]
    public void MatchesSearch_filters_version_ids(string query, string versionId, bool expected) =>
        Assert.Equal(expected, McVersionHelper.MatchesSearch(versionId, query));

    [Theory]
    [InlineData("1.20.1-pre1", "release", true)]
    [InlineData("1.19.4", "release", false)]
    [InlineData("b1.7.3", "old_beta", true)]
    public void IsExperimental_detects_prerelease_and_legacy_types(string id, string type, bool expected) =>
        Assert.Equal(expected, McVersionHelper.IsExperimental(id, type));

    [Theory]
    [InlineData("1.20.4", "1.20.4", 0)]
    [InlineData("1.21", "1.20.4", 1)]
    [InlineData("1.19", "1.20", -1)]
    public void CompareVersions_handles_equal_and_older_versions(string a, string b, int sign)
    {
        var result = Math.Sign(McVersionHelper.CompareVersions(a, b));
        Assert.Equal(sign, result);
    }
}
