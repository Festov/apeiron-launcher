using System.ComponentModel;
using System.Runtime.CompilerServices;
using Apeiron.Services;

namespace Apeiron.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private BuildInfo? _currentBuild;
    private bool _isDownloading;
    private string? _transientIcon;
    private string? _transientTextKey;
    private string _statusText = "";
    private string _playButtonIcon = "▶ ";
    private string _playButtonText = "";
    private bool _playButtonEnabled;
    private bool _isProgressVisible;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _progressText = "";
    private bool _isCancelDownloadVisible;
    private bool _isOpenInstallLogVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppVersionLabel => LauncherAppVersion.ShortDisplay;

    public BuildInfo? CurrentBuild
    {
        get => _currentBuild;
        set
        {
            if (ReferenceEquals(_currentBuild, value))
                return;

            _currentBuild = value;
            OnPropertyChanged();
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (_isDownloading == value)
                return;

            _isDownloading = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string PlayButtonIcon
    {
        get => _playButtonIcon;
        private set => SetField(ref _playButtonIcon, value);
    }

    public string PlayButtonText
    {
        get => _playButtonText;
        private set => SetField(ref _playButtonText, value);
    }

    public bool PlayButtonEnabled
    {
        get => _playButtonEnabled;
        private set => SetField(ref _playButtonEnabled, value);
    }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => SetField(ref _isProgressVisible, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetField(ref _isProgressIndeterminate, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetField(ref _progressValue, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetField(ref _progressText, value);
    }

    public bool IsCancelDownloadVisible
    {
        get => _isCancelDownloadVisible;
        private set => SetField(ref _isCancelDownloadVisible, value);
    }

    public bool IsOpenInstallLogVisible
    {
        get => _isOpenInstallLogVisible;
        private set => SetField(ref _isOpenInstallLogVisible, value);
    }

    public void SetStatus(string text) => StatusText = text;

    public void SetTransientPlayButton(string icon, string localizationKey, bool enabled = true)
    {
        _transientIcon = icon;
        _transientTextKey = localizationKey;
        ApplyPlayButton(icon, localizationKey, enabled);
    }

    public void SetPlayEnabled(bool enabled) => PlayButtonEnabled = enabled;

    public void ApplyDownloadProgress(int progress, string text)
    {
        var update = DownloadProgressHelper.CreateUpdate(progress, text);
        IsProgressVisible = true;
        IsProgressIndeterminate = !update.UpdateBarValue;
        if (update.UpdateBarValue)
            ProgressValue = update.BarValue;
        if (!string.IsNullOrEmpty(update.StatusText))
            ProgressText = update.StatusText;
    }

    public void ShowProgressPanel() => IsProgressVisible = true;

    public void HideProgressPanel()
    {
        IsProgressVisible = false;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        ProgressText = "";
    }

    public void BeginDownloadUi()
    {
        IsOpenInstallLogVisible = false;
        IsCancelDownloadVisible = true;
        ShowProgressPanel();
    }

    public void EndDownloadUi()
    {
        HideProgressPanel();
        IsCancelDownloadVisible = false;
    }

    public void ShowOpenInstallLog()
    {
        IsCancelDownloadVisible = false;
        IsOpenInstallLogVisible = true;
    }

    public void RefreshPlayState(string minecraftDir)
    {
        _transientIcon = null;
        _transientTextKey = null;

        var presentation = BuildUiState.GetPlayButtonPresentation(CurrentBuild, minecraftDir);
        ApplyPlayButton(presentation.Icon, presentation.LocalizationKey, presentation.IsEnabled);
        StatusText = GetStatusText(minecraftDir);
    }

    public void RefreshLocalizedText(string minecraftDir)
    {
        if (_transientTextKey != null)
            ApplyPlayButton(_transientIcon ?? "▶", _transientTextKey, PlayButtonEnabled);
        else
            RefreshPlayState(minecraftDir);
    }

    private void ApplyPlayButton(string icon, string localizationKey, bool enabled)
    {
        PlayButtonIcon = icon + (icon.Length > 1 ? "" : " ");
        PlayButtonText = LocalizationService.T(localizationKey);
        PlayButtonEnabled = enabled;
    }

    private string GetStatusText(string minecraftDir)
    {
        if (CurrentBuild == null)
            return LocalizationService.T("main.ready");

        var key = BuildUiState.GetStatusLocalizationKey(CurrentBuild, minecraftDir);
        return LocalizationService.F(key, CurrentBuild.DisplayName);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
