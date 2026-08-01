using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class CurseForgeModpackServiceTests
{
    [Theory]
    [InlineData(6963018, "FastItemFrames.jar", "https://edge.forgecdn.net/files/6963/18/FastItemFrames.jar")]
    [InlineData(8287097, "entityculling-neoforge-1.10.5-mc1.21.1.jar",
        "https://edge.forgecdn.net/files/8287/97/entityculling-neoforge-1.10.5-mc1.21.1.jar")]
    [InlineData(8312220, "servercore-neoforge-1.5.19+1.21.1.jar",
        "https://edge.forgecdn.net/files/8312/220/servercore-neoforge-1.5.19%2B1.21.1.jar")]
    public void BuildCdnDownloadUrl_matches_curseforge_layout(int fileId, string fileName, string expected) =>
        Assert.Equal(expected, CurseForgeModpackService.BuildCdnDownloadUrl(fileId, fileName));

    [Fact]
    public void BuildCdnDownloadUrl_rejects_invalid_input()
    {
        Assert.Equal("", CurseForgeModpackService.BuildCdnDownloadUrl(0, "a.jar"));
        Assert.Equal("", CurseForgeModpackService.BuildCdnDownloadUrl(1, ""));
        Assert.Equal("", CurseForgeModpackService.BuildCdnDownloadUrl(1, "   "));
    }
}
