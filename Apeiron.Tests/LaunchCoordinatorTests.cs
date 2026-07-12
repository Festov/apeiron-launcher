using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LaunchCoordinatorTests
{
    [Fact]
    public void ResolveOfflineIdentity_saves_sanitized_username()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-launch-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            var settings = new SettingsService(root);

            var identity = LaunchCoordinator.ResolveOfflineIdentity(settings, "Test_User01");

            Assert.True(identity.IsOffline);
            Assert.Equal("Test_User01", identity.Username);
            Assert.Equal("offline", identity.AccessToken);
            Assert.Equal(GameLaunchService.GetOfflineUuid("Test_User01"), identity.Uuid);

            settings.Load();
            Assert.Equal("Test_User01", settings.OfflineUsername);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ResolveOfflineIdentity_sanitizes_invalid_input()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-launch-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            var settings = new SettingsService(root);

            var identity = LaunchCoordinator.ResolveOfflineIdentity(settings, "Иван");

            Assert.Equal(OfflineUsernameHelper.Default, identity.Username);
            Assert.Equal(OfflineUsernameHelper.Default, settings.OfflineUsername);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
