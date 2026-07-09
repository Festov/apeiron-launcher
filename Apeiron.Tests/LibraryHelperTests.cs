using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LibraryHelperTests
{
    [Fact]
    public void BuildMavenJarUrl_builds_fabric_loader_url()
    {
        var url = LibraryHelper.BuildMavenJarUrl(
            "net.fabricmc:fabric-loader:0.19.3",
            "https://maven.fabricmc.net/");

        Assert.Equal(
            "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.19.3/fabric-loader-0.19.3.jar",
            url);
    }
}
