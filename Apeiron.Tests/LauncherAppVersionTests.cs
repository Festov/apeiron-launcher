using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LauncherAppVersionTests
{
    [Fact]
    public void ShortDisplay_uses_major_minor_from_assembly()
    {
        var version = LauncherAppVersion.Current;
        Assert.Equal($"v{version.Major}.{version.Minor}", LauncherAppVersion.ShortDisplay);
    }
}
