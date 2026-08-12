using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class AuthServiceHelperTests
{
    [Theory]
    [InlineData("https://login.live.com/oauth20_desktop.srf?code=AbCd.Ef-123&lc=1033", "AbCd.Ef-123")]
    [InlineData("code=AbCd.Ef-123&lc=1033", "AbCd.Ef-123")]
    [InlineData("AbCd.Ef-123_rawcodevalueXX", "AbCd.Ef-123_rawcodevalueXX")]
    [InlineData("", null)]
    [InlineData("https://example.com/", null)]
    public void TryExtractAuthCode_parses_url_query_and_raw(string input, string? expected)
    {
        Assert.Equal(expected, AuthService.TryExtractAuthCode(input));
    }

    [Fact]
    public void BuildAuthorizeUrl_uses_minecraft_client_and_desktop_redirect()
    {
        var url = AuthService.BuildAuthorizeUrl();
        Assert.Contains(AuthService.ClientId, url);
        Assert.Contains("oauth20_desktop.srf", url);
        Assert.Contains("XboxLive.signin", url);
    }
}
