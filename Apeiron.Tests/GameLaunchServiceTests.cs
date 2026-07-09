using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class GameLaunchServiceTests
{
    [Fact]
    public void CreateOfflineIdentity_sanitizes_username_and_sets_offline_token()
    {
        var identity = GameLaunchService.CreateOfflineIdentity("Player_01");

        Assert.Equal("Player_01", identity.Username);
        Assert.Equal("offline", identity.AccessToken);
        Assert.True(identity.IsOffline);
        Assert.False(string.IsNullOrWhiteSpace(identity.Uuid));
    }

    [Fact]
    public void GetOfflineUuid_is_deterministic_for_same_username()
    {
        var first = GameLaunchService.GetOfflineUuid("Steve");
        var second = GameLaunchService.GetOfflineUuid("Steve");

        Assert.Equal(first, second);
        Assert.NotEqual(GameLaunchService.GetOfflineUuid("Alex"), first);
    }

    [Fact]
    public void CreateOnlineIdentity_preserves_session_fields()
    {
        var identity = GameLaunchService.CreateOnlineIdentity("Steve", "uuid-123", "token-abc");

        Assert.Equal("Steve", identity.Username);
        Assert.Equal("uuid-123", identity.Uuid);
        Assert.Equal("token-abc", identity.AccessToken);
        Assert.False(identity.IsOffline);
    }
}
