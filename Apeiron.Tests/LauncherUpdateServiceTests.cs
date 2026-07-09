using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LauncherUpdateServiceTests
{
    [Theory]
    [InlineData("1.4.0", "1.3.0", true)]
    [InlineData("1.3.0", "1.3.0", false)]
    [InlineData("1.3.1", "1.3.0", true)]
    [InlineData("1.2.9", "1.3.0", false)]
    public void IsNewerVersion_compares_semver_parts(string latest, string current, bool expected)
    {
        var result = LauncherUpdateService.IsNewerVersion(
            Version.Parse(latest),
            Version.Parse(current));

        Assert.Equal(expected, result);
    }
}
