using System;

namespace Apeiron.Services;

public static class LauncherAppVersion
{
    public static Version Current => LauncherUpdateService.GetCurrentVersion();

    public static string ShortDisplay => $"v{Current.Major}.{Current.Minor}";

    public static string FullDisplay => $"{Current.Major}.{Current.Minor}.{Current.Build}";
}
