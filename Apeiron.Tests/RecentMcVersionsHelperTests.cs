using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class RecentMcVersionsHelperTests
{
    [Fact]
    public void Record_moves_version_to_front_and_limits_count()
    {
        var recent = new List<string> { "1.21", "1.20.4", "1.20.1" };

        RecentMcVersionsHelper.Record(recent, "1.20.4");

        Assert.Equal("1.20.4", recent[0]);
        Assert.Equal(3, recent.Count);

        for (var i = 0; i < RecentMcVersionsHelper.MaxCount + 2; i++)
            RecentMcVersionsHelper.Record(recent, $"1.{i}.0");

        Assert.Equal(RecentMcVersionsHelper.MaxCount, recent.Count);
        Assert.DoesNotContain("1.20.1", recent);
    }

    [Fact]
    public void OrderWithRecentFirst_puts_recent_versions_first()
    {
        var ordered = RecentMcVersionsHelper.OrderWithRecentFirst(
            new[] { "1.20.1", "1.21", "1.20.4", "1.19.4" },
            new[] { "1.21", "1.19.4" });

        Assert.Equal(new[] { "1.21", "1.19.4", "1.20.1", "1.20.4" }, ordered);
    }
}
