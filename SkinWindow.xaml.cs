using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Apeiron.Services;
using Microsoft.Win32;

namespace Apeiron;

public partial class SkinWindow : Window, ILocalizable
{
    private readonly AuthService _auth;
    private readonly SkinService _skins;
    private readonly SettingsService _settings;

    private string _presetId;
    private string? _pendingCustomPath;
    private AccountSkinInfo? _selectedHistory;
    private BitmapImage? _accountFullSkin;
    private bool _accountSkinSlim;
    private BitmapImage? _historyFullSkin;
    private bool _busy;
    private bool _showingAccountSkin;
    private bool _modelLockedToPreset;

    public bool Applied { get; private set; }

    public SkinWindow(AuthService auth, SkinService skins, SettingsService settings)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        RoundedDialogChrome.Attach(DialogChrome);

        _auth = auth;
        _skins = skins;
        _settings = settings;

        _presetId = NormalizePresetId(settings.SkinPreset);
        if (_presetId == "custom" && !string.IsNullOrWhiteSpace(settings.CustomSkinPath))
            _pendingCustomPath = settings.CustomSkinPath;

        var standards = _skins.GetStandardSkins();
        ClassicPresetList.ItemsSource = standards.Where(s => !s.IsSlim).ToList();
        SlimPresetList.ItemsSource = standards.Where(s => s.IsSlim).ToList();

        if (SkinService.IsStandardSkinId(_presetId))
            LockModelTo(SkinService.IsSlimStandard(_presetId));
        else if (string.Equals(settings.SkinModel, "slim", StringComparison.OrdinalIgnoreCase))
            SetModelChoice(slim: true, locked: false);
        else
            SetModelChoice(slim: false, locked: false);

        Loaded += async (_, _) =>
        {
            ApplyLocalization();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            await InitializePreviewAsync();
            await LoadHistoryAsync();
        };
        Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(ApplyLocalization);

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("skin.window_title");
        TitleText.Text = LocalizationService.T("skin.window_title");
        PresetsLabel.Text = LocalizationService.T("skin.presets");
        ClassicPresetsLabel.Text = LocalizationService.T("skin.classic");
        SlimPresetsLabel.Text = LocalizationService.T("skin.slim");
        BrowseButton.Content = LocalizationService.T("skin.browse");
        ClassicRadio.Content = LocalizationService.T("skin.classic");
        SlimRadio.Content = LocalizationService.T("skin.slim");
        CancelButton.Content = LocalizationService.T("common.cancel");
        ApplyButton.Content = LocalizationService.T("skin.apply");
        HistoryLabel.Text = LocalizationService.T("skin.history");
        HintText.Text = _auth.IsAuthenticated()
            ? LocalizationService.T("skin.hint_online")
            : LocalizationService.T("skin.hint_offline");
        Preview3D.SetDragHint(LocalizationService.T("skin.drag_hint"));
    }

    private void ClearStatus()
    {
        StatusText.Text = "";
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void SetModelChoice(bool slim, bool locked)
    {
        _modelLockedToPreset = locked;
        ModelRadioRow.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        ClassicRadio.IsEnabled = !locked;
        SlimRadio.IsEnabled = !locked;

        if (slim)
            SlimRadio.IsChecked = true;
        else
            ClassicRadio.IsChecked = true;
    }

    private void LockModelTo(bool slim) => SetModelChoice(slim, locked: true);

    private async System.Threading.Tasks.Task InitializePreviewAsync()
    {
        ClearStatus();

        if (_auth.IsAuthenticated())
        {
            var token = _auth.GetAccessToken();
            var active = await _skins.TryLoadActiveAccountFullSkinAsync(token);
            if (active != null)
            {
                _accountFullSkin = active.Value.Skin;
                _accountSkinSlim = active.Value.IsSlim;
                _showingAccountSkin = true;
                _presetId = "default";
                SetModelChoice(_accountSkinSlim, locked: false);
                Preview3D.SetSkin(_accountFullSkin, _accountSkinSlim);
                return;
            }
        }

        _showingAccountSkin = false;
        if (SkinService.IsStandardSkinId(_presetId))
            LockModelTo(SkinService.IsSlimStandard(_presetId));
        RefreshPreview();
    }

    private async System.Threading.Tasks.Task LoadHistoryAsync()
    {
        HistorySection.Visibility = Visibility.Visible;

        if (!_auth.IsAuthenticated())
        {
            HistoryList.ItemsSource = null;
            return;
        }

        var token = _auth.GetAccessToken();
        if (string.IsNullOrEmpty(token))
        {
            HistoryList.ItemsSource = null;
            return;
        }

        var skins = await _skins.GetAccountSkinsAsync(token);
        HistoryList.ItemsSource = skins.Count > 0 ? skins : null;

        var active = skins.FirstOrDefault(s => s.IsActive);
        if (_showingAccountSkin && active != null && _accountFullSkin != null)
        {
            _selectedHistory = active;
            _historyFullSkin = _accountFullSkin;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void StandardSkin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StandardSkinInfo skin })
            return;

        ClearHistorySelection();
        ClearStatus();
        _showingAccountSkin = false;
        _presetId = skin.Id;
        _pendingCustomPath = null;
        LockModelTo(skin.IsSlim);
        RefreshPreview();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Title = LocalizationService.T("skin.browse_title")
        };

        if (dialog.ShowDialog(this) != true)
            return;

        if (!SkinService.TryValidateSkinFile(dialog.FileName, out var error))
        {
            ShowError(error);
            return;
        }

        ClearHistorySelection();
        ClearStatus();
        _showingAccountSkin = false;
        _presetId = "custom";
        _pendingCustomPath = dialog.FileName;
        SetModelChoice(SlimRadio.IsChecked == true, locked: false);
        RefreshPreview();
    }

    private async void HistorySkin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountSkinInfo skin })
            return;

        ClearStatus();
        _selectedHistory = skin;
        _showingAccountSkin = skin.IsActive;
        _presetId = "custom";
        _pendingCustomPath = null;
        SetModelChoice(skin.IsSlim, locked: false);

        _historyFullSkin = await _skins.DownloadFullSkinFromUrlAsync(skin.Url);
        if (skin.IsActive)
            _accountFullSkin = _historyFullSkin;
        Preview3D.SetSkin(_historyFullSkin, SlimRadio.IsChecked == true);
    }

    private void ClearHistorySelection()
    {
        _selectedHistory = null;
        _historyFullSkin = null;
    }

    private void ModelRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _modelLockedToPreset)
            return;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_selectedHistory != null && _historyFullSkin != null)
        {
            Preview3D.SetSkin(_historyFullSkin, SlimRadio.IsChecked == true);
            return;
        }

        if (_showingAccountSkin && _accountFullSkin != null)
        {
            Preview3D.SetSkin(_accountFullSkin, SlimRadio.IsChecked == true);
            return;
        }

        var path = ResolveSkinFilePath();
        var skin = _skins.LoadFullSkinBitmap(path);
        Preview3D.SetSkin(skin, SlimRadio.IsChecked == true);
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        _busy = true;
        ApplyButton.IsEnabled = false;
        ClearStatus();

        try
        {
            var model = SlimRadio.IsChecked == true ? SkinModel.Slim : SkinModel.Classic;

            if (_auth.IsAuthenticated())
            {
                var token = _auth.GetAccessToken();
                if (string.IsNullOrEmpty(token))
                    throw new InvalidOperationException(LocalizationService.T("skin.not_signed_in"));

                if (_showingAccountSkin && _selectedHistory is { IsActive: true } &&
                    _pendingCustomPath == null && !SkinService.IsStandardSkinId(_presetId))
                {
                    if (_selectedHistory != null &&
                        ((_selectedHistory.IsSlim && model == SkinModel.Classic) ||
                         (!_selectedHistory.IsSlim && model == SkinModel.Slim)))
                    {
                        await _skins.ActivateSkinByUrlAsync(token, _selectedHistory.Url, model);
                        _skins.InvalidateUuidCache(_auth.GetUUID());
                    }

                    _settings.SkinPreset = "default";
                    _settings.CustomSkinPath = "";
                    _settings.SkinModel = model == SkinModel.Slim ? "slim" : "classic";
                    _settings.Save();
                    Applied = true;
                    DialogResult = true;
                    Close();
                    return;
                }

                if (_selectedHistory != null && _pendingCustomPath == null && !SkinService.IsStandardSkinId(_presetId))
                {
                    await _skins.ActivateSkinByUrlAsync(token, _selectedHistory.Url, model);
                }
                else
                {
                    var skinPath = ResolveSkinFilePath();
                    if (skinPath == null)
                        throw new InvalidOperationException(LocalizationService.T("skin.file_missing"));
                    await _skins.UploadSkinAsync(token, skinPath, model);
                }

                _skins.InvalidateUuidCache(_auth.GetUUID());
                _settings.SkinPreset = "default";
                _settings.CustomSkinPath = "";
            }
            else
            {
                if (_presetId == "custom")
                {
                    var skinPath = ResolveSkinFilePath();
                    if (skinPath == null)
                        throw new InvalidOperationException(LocalizationService.T("skin.file_missing"));
                    var saved = _skins.SaveCustomSkin(skinPath);
                    _settings.CustomSkinPath = saved;
                    _settings.SkinPreset = "custom";
                }
                else
                {
                    _settings.SkinPreset = SkinService.IsStandardSkinId(_presetId) ? _presetId : "steve";
                    _settings.CustomSkinPath = "";
                }
            }

            _settings.SkinModel = model == SkinModel.Slim ? "slim" : "classic";
            _settings.Save();

            Applied = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
            ApplyButton.IsEnabled = true;
        }
    }

    private string? ResolveSkinFilePath()
    {
        if (_presetId == "custom")
            return _pendingCustomPath ?? _settings.CustomSkinPath;

        if (_presetId == "default")
            return null;

        return _skins.GetStandardSkinPath(_presetId);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string NormalizePresetId(string? value)
    {
        var id = value?.Trim().ToLowerInvariant();
        if (id is "custom" or "default")
            return id;
        if (SkinService.IsStandardSkinId(id))
            return id!;
        return "steve";
    }
}
