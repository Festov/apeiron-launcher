using System.Threading.Tasks;

namespace Apeiron.Services;

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
}
