using System.Threading.Tasks;

namespace Apeiron.Services;

public enum LaunchIdentityStatus
{
    Ready,
    SessionExpired
}

public readonly record struct LaunchIdentityResolution(LaunchIdentityStatus Status, LaunchIdentity Identity);

/// <summary>Launch-time helpers extracted from MainWindow.</summary>
public static class LaunchCoordinator
{
    public static void SaveOfflineUsernameBeforeLaunch(SettingsService settings, string textBoxValue)
    {
        var name = OfflineUsernameHelper.Sanitize(textBoxValue);
        settings.OfflineUsername = name;
        settings.Save();
    }

    public static LaunchIdentity ResolveOfflineIdentity(SettingsService settings, string textBoxValue)
    {
        SaveOfflineUsernameBeforeLaunch(settings, textBoxValue);
        return GameLaunchService.CreateOfflineIdentity(settings.OfflineUsername);
    }

    public static async Task<LaunchIdentityResolution> ResolveLaunchIdentityAsync(
        AuthService auth,
        SettingsService settings,
        string offlineTextBoxValue)
    {
        if (auth.IsAuthenticated())
        {
            if (!await auth.EnsureValidSessionAsync())
                return new LaunchIdentityResolution(LaunchIdentityStatus.SessionExpired, default);

            return new LaunchIdentityResolution(
                LaunchIdentityStatus.Ready,
                GameLaunchService.CreateOnlineIdentity(
                    auth.GetUsername()!,
                    auth.GetUUID()!,
                    auth.GetAccessToken()!));
        }

        return new LaunchIdentityResolution(
            LaunchIdentityStatus.Ready,
            ResolveOfflineIdentity(settings, offlineTextBoxValue));
    }
}
