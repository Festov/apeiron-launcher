using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Apeiron.Services;

namespace Apeiron;

public partial class ModBrowserWindow : Window, ILocalizable
{
    private readonly BuildInfo _build;
    private readonly SettingsService _settings;
    private readonly ModrinthModpackService _modrinth = new();
    private CancellationTokenSource? _searchCts;
    private int _searchRequestId;
    private System.Windows.Threading.DispatcherTimer? _searchDebounce;
    private bool _busy;

    public bool Installed { get; private set; }

    public ModBrowserWindow(BuildInfo build, SettingsService settings)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        RoundedDialogChrome.Attach(DialogChrome);
        _build = build;
        _settings = settings;

        Loaded += async (_, _) =>
        {
            ApplyLocalization();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            await LoadModsAsync();
        };
        Closed += (_, _) =>
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
            _searchDebounce?.Stop();
        };
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(() =>
    {
        var platformIdx = SourceComboBox.SelectedIndex;
        ApplyLocalization();
        SourceComboBox.SelectedIndex = platformIdx < 0 ? 0 : Math.Min(platformIdx, 2);
    });

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("mod_browser.window_title");
        TitleText.Text = LocalizationService.T("mod_browser.title");
        PlatformLabel.Text = LocalizationService.T("add_build.modpack.platform");
        SearchLabel.Text = LocalizationService.T("mod_browser.search");
        SearchTextBox.ToolTip = LocalizationService.T("mod_browser.search_hint");
        ListLabel.Text = LocalizationService.T("mod_browser.list");
        RefreshButton.ToolTip = LocalizationService.T("mod_browser.refresh");
        InstallButton.Content = LocalizationService.T("mod_browser.install");
        CancelBtn.Content = LocalizationService.T("common.cancel");

        var platformIdx = SourceComboBox.SelectedIndex;
        SourceComboBox.ItemsSource = new[]
        {
            LocalizationService.T("add_build.modpack.platform.both"),
            LocalizationService.T("add_build.modpack.platform.modrinth"),
            LocalizationService.T("add_build.modpack.platform.curseforge")
        };
        SourceComboBox.SelectedIndex = platformIdx < 0 ? 0 : Math.Min(platformIdx, 2);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void ModsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InstallButton.IsEnabled = !_busy && ModsListBox.SelectedItem is ModpackListItem;

    private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UiAsync.Run(LoadModsAsync, Dispatcher);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _searchDebounce ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _searchDebounce.Stop();
        _searchDebounce.Tick -= SearchDebounce_Tick;
        _searchDebounce.Tick += SearchDebounce_Tick;
        _searchDebounce.Start();
    }

    private void SearchDebounce_Tick(object? sender, EventArgs e)
    {
        _searchDebounce?.Stop();
        UiAsync.Run(LoadModsAsync, Dispatcher);
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _searchDebounce?.Stop();
        UiAsync.Run(LoadModsAsync, Dispatcher);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        UiAsync.Run(LoadModsAsync, Dispatcher);

    private async Task LoadModsAsync()
    {
        if (ModsListBox == null || StatusText == null)
            return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = _searchCts.Token;
        var requestId = ++_searchRequestId;
        var query = SearchTextBox?.Text?.Trim() ?? "";
        // 0 = Both, 1 = Modrinth, 2 = CurseForge
        var platformMode = SourceComboBox.SelectedIndex;

        SetBusy(true);
        ModsListBox.ItemsSource = null;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = LocalizationService.T("mod_browser.searching");

        try
        {
            IReadOnlyList<ModpackListItem> items = platformMode switch
            {
                2 => await LoadCurseForgeModsAsync(query, limit: 100, token),
                1 => await _modrinth.SearchModsAsync(query, limit: 100, cancellationToken: token),
                _ => await LoadBothPlatformsAsync(query, token)
            };

            if (requestId != _searchRequestId)
                return;

            var showPlatform = platformMode == 0;
            foreach (var item in items)
                item.ShowPlatformInSubtitle = showPlatform;

            ModsListBox.ItemsSource = items;
            if (items.Count == 0)
            {
                StatusText.Text = LocalizationService.T("mod_browser.no_results");
                StatusText.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = LocalizationService.F("mod_browser.results", items.Count);
                StatusText.Visibility = Visibility.Visible;
                _ = ModpackIconLoader.LoadAllAsync(items, token);
            }
        }
        catch (OperationCanceledException)
        {
            // newer request
        }
        catch (Exception ex)
        {
            if (requestId != _searchRequestId)
                return;
            ModsListBox.ItemsSource = null;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = ex.Message;
        }
        finally
        {
            if (requestId == _searchRequestId)
                SetBusy(false);
        }
    }

    private async Task<IReadOnlyList<ModpackListItem>> LoadCurseForgeModsAsync(
        string query,
        int limit,
        CancellationToken token)
    {
        var cf = new CurseForgeModpackService(_settings.CurseForgeApiKey);
        if (!cf.HasApiKey)
            throw new InvalidOperationException(LocalizationService.T("add_build.modpack.curseforge_key_missing"));
        return await cf.SearchModsAsync(query, limit, token);
    }

    private async Task<IReadOnlyList<ModpackListItem>> LoadBothPlatformsAsync(
        string query,
        CancellationToken token)
    {
        const int perPlatform = 50;
        var modrinthTask = _modrinth.SearchModsAsync(query, limit: perPlatform, cancellationToken: token);

        Task<IReadOnlyList<ModpackListItem>>? curseTask = null;
        Exception? curseError = null;
        try
        {
            curseTask = LoadCurseForgeModsAsync(query, limit: perPlatform, token);
        }
        catch (Exception ex)
        {
            curseError = ex;
        }

        var modrinth = await modrinthTask;
        IReadOnlyList<ModpackListItem> curse = Array.Empty<ModpackListItem>();
        if (curseTask != null)
        {
            try
            {
                curse = await curseTask;
            }
            catch (Exception ex)
            {
                curseError = ex;
            }
        }

        if (modrinth.Count == 0 && curse.Count == 0 && curseError != null)
            throw curseError;

        return modrinth
            .Concat(curse)
            .OrderByDescending(i => i.Downloads)
            .Take(100)
            .ToList();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || ModsListBox.SelectedItem is not ModpackListItem item)
            return;

        SetBusy(true);
        StatusText.Text = LocalizationService.T("mod_browser.installing");

        try
        {
            var modsDir = _build.GetModsDir();
            Directory.CreateDirectory(modsDir);

            if (item.Source == ModpackSource.CurseForge)
            {
                if (!int.TryParse(item.Id, out var modId))
                    throw new InvalidOperationException(LocalizationService.T("add_build.modpack.invalid_id"));

                var cf = new CurseForgeModpackService(_settings.CurseForgeApiKey);
                var (_, fileId, fileName, downloadUrl) = await cf.ResolveLatestModFileAsync(modId);
                var dest = Path.Combine(modsDir, Path.GetFileName(fileName));
                await cf.DownloadModFileAsync(modId, fileId, dest, downloadUrl, fileName);
            }
            else
            {
                var (_, downloadUrl, fileName) = await _modrinth.ResolveLatestModFileAsync(item.Id);
                var dest = Path.Combine(modsDir, Path.GetFileName(fileName));
                await _modrinth.DownloadFileAsync(downloadUrl, dest);
            }

            Installed = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        SourceComboBox.IsEnabled = !busy;
        SearchTextBox.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy && ModsListBox.SelectedItem is ModpackListItem;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
