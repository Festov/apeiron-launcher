using Apeiron.Services;

namespace Apeiron.ViewModels;

public sealed class MainViewModel
{
    public BuildInfo? CurrentBuild { get; set; }
    public bool IsDownloading { get; set; }

    public PlayButtonPresentation GetPlayButton(string minecraftDir) =>
        BuildUiState.GetPlayButtonPresentation(CurrentBuild, minecraftDir);

    public string GetStatusText(string minecraftDir)
    {
        if (CurrentBuild == null)
            return LocalizationService.T("main.ready");

        var key = BuildUiState.GetStatusLocalizationKey(CurrentBuild, minecraftDir);
        return LocalizationService.F(key, CurrentBuild.DisplayName);
    }

    public PlayButtonPresentation GetRestorePlayButton(string minecraftDir) =>
        GetPlayButton(minecraftDir);
}
