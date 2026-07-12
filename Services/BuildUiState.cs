namespace Apeiron.Services;

public enum PlayButtonMode
{
    NoBuilds,
    Download,
    Play
}

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

    public static bool IsBuildInstalled(BuildInfo? build, string minecraftDir) =>
        build != null && BuildInstallService.IsInstalled(minecraftDir, build);
}
