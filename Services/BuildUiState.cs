namespace Apeiron.Services;

public enum PlayButtonMode
{
    NoBuilds,
    Download,
    Play
}

public readonly record struct PlayButtonPresentation(string Icon, string LocalizationKey, bool IsEnabled);

public static class BuildUiState
{
    public static PlayButtonMode GetPlayButtonMode(BuildInfo? build, string minecraftDir)
    {
        if (build == null)
            return PlayButtonMode.NoBuilds;

        if (!BuildInstallService.IsInstalled(minecraftDir, build))
            return PlayButtonMode.Download;

        return PlayButtonMode.Play;
    }

    public static (string Icon, string LocalizationKey) GetPlayButtonContent(PlayButtonMode mode) =>
        mode switch
        {
            PlayButtonMode.NoBuilds => ("⚠️", "main.no_builds_short"),
            PlayButtonMode.Download => ("⬇️", "main.download"),
            _ => ("▶", "main.play")
        };

    public static PlayButtonPresentation GetPlayButtonPresentation(BuildInfo? build, string minecraftDir)
    {
        if (build == null)
            return new PlayButtonPresentation("⚠️", "main.no_builds_short", false);

        var (icon, key) = GetPlayButtonContent(GetPlayButtonMode(build, minecraftDir));
        return new PlayButtonPresentation(icon, key, true);
    }

    public static string GetStatusLocalizationKey(BuildInfo? build, string minecraftDir) =>
        build == null
            ? "main.ready"
            : GetPlayButtonMode(build, minecraftDir) == PlayButtonMode.Play
                ? "main.build_ready"
                : "main.build_download_hint";

    public static bool IsBuildInstalled(BuildInfo? build, string minecraftDir) =>
        build != null && BuildInstallService.IsInstalled(minecraftDir, build);
}
