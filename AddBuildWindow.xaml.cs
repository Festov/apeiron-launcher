using Apeiron.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json.Linq;

namespace Apeiron;

public partial class AddBuildWindow : Window, ILocalizable
{
    private sealed class VersionEntry
    {
        public string Id { get; init; } = "";
        public string Type { get; init; } = "";
        public DateTime ReleaseTime { get; init; }
        public bool IsStable => McVersionHelper.IsStableRelease(Id, Type);
    }

    public sealed class VersionListItem
    {
        public string Id { get; init; } = "";
        public string Display { get; init; } = "";
        public override string ToString() => Display;
    }

    private readonly SettingsService _settings;
    private readonly LoaderService _loaderService;
    public BuildInfo? CreatedBuild { get; private set; }
    private List<VersionEntry> _allVersions = new();
    private bool _isLoadingVersions;
    private string _versionSearchQuery = "";
    private CancellationTokenSource? _loaderVersionsCts;
    private int _loaderVersionsRequestId;

    public AddBuildWindow(SettingsService settings)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _settings = settings;
        _loaderService = new LoaderService(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft"));

        LoaderComboBox.ItemsSource = new[] { "Fabric", "Forge", "NeoForge", "Quilt" };
        LoaderComboBox.SelectedIndex = 0;

        Loaded += async (_, _) =>
        {
            ApplyLocalization();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            await LoadVersions();
            await OnTypeSelectionChangedAsync();
        };
        Closed += (_, _) =>
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _loaderVersionsCts?.Cancel();
            _loaderVersionsCts?.Dispose();
            _loaderVersionsCts = null;
        };
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(() =>
    {
        var wasModded = IsModdedSelected();
        ApplyLocalization();
        TypeComboBox.SelectedIndex = wasModded ? 1 : 0;
    });

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("add_build.window_title");
        TitleText.Text = LocalizationService.T("add_build.title");
        NameLabel.Text = LocalizationService.T("add_build.name");
        NameHintText.Text = LocalizationService.T("add_build.name_hint");
        TypeLabel.Text = LocalizationService.T("add_build.type");
        LoaderLabel.Text = LocalizationService.T("add_build.loader");
        McVersionLabel.Text = LocalizationService.T("add_build.mc_version");
        VersionSearchLabel.Text = LocalizationService.T("add_build.search_versions");
        if (VersionSearchTextBox != null)
            VersionSearchTextBox.ToolTip = LocalizationService.T("add_build.search_versions_hint");
        FilterLabel.Text = LocalizationService.T("add_build.filter");
        ShowSnapshotsCheckBox.Content = LocalizationService.T("add_build.show_snapshots");
        LoaderVersionLabel.Text = LocalizationService.T("add_build.loader_version");
        RefreshVersionsButton.ToolTip = LocalizationService.T("add_build.refresh_versions");
        CancelButton.Content = LocalizationService.T("common.cancel");
        CreateButton.Content = LocalizationService.T("common.create");

        var idx = TypeComboBox.SelectedIndex;
        TypeComboBox.ItemsSource = new[] { LocalizationService.T("add_build.type.vanilla"), LocalizationService.T("add_build.type.modded") };
        TypeComboBox.SelectedIndex = idx < 0 ? 0 : idx;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void BuildNameTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCreateButtonState();

    private bool IsModdedSelected() => TypeComboBox.SelectedIndex == 1;

    private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UiAsync.Run(OnTypeSelectionChangedAsync, Dispatcher);

    private async Task OnTypeSelectionChangedAsync()
    {
        if (LoaderLabel == null || LoaderComboBox == null ||
            LoaderVersionLabel == null || LoaderVersionComboBox == null ||
            LoaderRow == null || LoaderVersionRow == null)
            return;

        bool isModded = IsModdedSelected();

        LoaderRow.Visibility = isModded ? Visibility.Visible : Visibility.Collapsed;
        LoaderVersionRow.Visibility = isModded ? Visibility.Visible : Visibility.Collapsed;

        if (isModded)
            await LoadLoaderVersions();

        UpdateCreateButtonState();
    }

    private void LoaderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UiAsync.Run(OnLoaderSelectionChangedAsync, Dispatcher);

    private async Task OnLoaderSelectionChangedAsync()
    {
        await LoadLoaderVersions();
        UpdateCreateButtonState();
    }

    private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UiAsync.Run(OnVersionSelectionChangedAsync, Dispatcher);

    private async Task OnVersionSelectionChangedAsync()
    {
        UpdateCreateButtonState();
        if (IsModdedSelected())
            await LoadLoaderVersions();
    }

    private static bool IsLoaderPlaceholder(string value) =>
        value.StartsWith("⏳") || value.StartsWith("❌");

    private void UpdateCreateButtonState()
    {
        if (CreateButton == null) return;

        bool hasName = true;
        bool hasVersion = VersionComboBox?.SelectedItem is VersionListItem v && !string.IsNullOrEmpty(v.Id);

        bool isModded = IsModdedSelected();
        bool hasLoader = !isModded || LoaderComboBox?.SelectedItem is string;
        bool hasLoaderVersion = !isModded || (LoaderVersionComboBox?.SelectedItem is string lv &&
                                                !IsLoaderPlaceholder(lv));

        CreateButton.IsEnabled = hasName && hasVersion && hasLoader && hasLoaderVersion;
    }

    private void RefreshVersionsButton_Click(object sender, RoutedEventArgs e) =>
        UiAsync.Run(RefreshVersionsAsync, Dispatcher);

    private async Task RefreshVersionsAsync() => await LoadVersions();

    private void ShowSnapshotsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingVersions || _allVersions.Count == 0) return;
        PopulateVersionComboBox();
    }

    private void VersionSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _versionSearchQuery = VersionSearchTextBox.Text.Trim();
        if (_isLoadingVersions || _allVersions.Count == 0)
            return;

        PopulateVersionComboBox();
    }

    private void PopulateVersionComboBox()
    {
        var showAll = ShowSnapshotsCheckBox?.IsChecked == true;
        var previousId = (VersionComboBox.SelectedItem as VersionListItem)?.Id;

        VersionComboBox.ItemsSource = null;

        var filtered = _allVersions
            .Where(v => showAll || v.IsStable)
            .Where(v => McVersionHelper.MatchesSearch(v.Id, _versionSearchQuery))
            .Select(entry => new VersionListItem
            {
                Id = entry.Id,
                Display = entry.IsStable ? entry.Id : $"{entry.Id} · {GetTypeLabel(entry.Type)}"
            })
            .ToList();

        if (string.IsNullOrEmpty(_versionSearchQuery) && _settings.RecentMcVersions.Count > 0)
        {
            var orderedIds = RecentMcVersionsHelper.OrderWithRecentFirst(
                filtered.Select(item => item.Id),
                _settings.RecentMcVersions);

            filtered = orderedIds
                .Select(id =>
                {
                    var item = filtered.First(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    var isRecent = _settings.RecentMcVersions.Any(v => v.Equals(id, StringComparison.OrdinalIgnoreCase));
                    return isRecent
                        ? new VersionListItem { Id = item.Id, Display = $"★ {item.Display}" }
                        : item;
                })
                .ToList();
        }

        if (filtered.Count == 0)
        {
            var emptyMessage = string.IsNullOrEmpty(_versionSearchQuery)
                ? (showAll ? LocalizationService.T("add_build.no_versions") : LocalizationService.T("add_build.no_stable_versions"))
                : LocalizationService.F("add_build.no_search_results", _versionSearchQuery);
            VersionComboBox.ItemsSource = new[] { new VersionListItem { Id = "", Display = emptyMessage } };
            VersionComboBox.IsEnabled = false;
            return;
        }

        VersionComboBox.IsEnabled = true;
        VersionComboBox.ItemsSource = filtered;

        var selected = filtered.FirstOrDefault(v => v.Id == previousId) ?? filtered[0];
        VersionComboBox.SelectedItem = selected;

        UpdateCreateButtonState();
    }

    private static string GetTypeLabel(string type) => type switch
    {
        "snapshot" => LocalizationService.T("add_build.type.snapshot"),
        "old_beta" => "beta",
        "old_alpha" => "alpha",
        _ => LocalizationService.T("add_build.type.prerelease")
    };

    private async Task LoadVersions()
    {
        if (_isLoadingVersions) return;
        _isLoadingVersions = true;

        try
        {
            RefreshVersionsButton.IsEnabled = false;
            VersionComboBox.ItemsSource = new[] { new VersionListItem { Id = "", Display = LocalizationService.T("add_build.loading") } };
            VersionComboBox.IsEnabled = false;

            var manifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
            var json = await AppHttp.Client.GetStringAsync(manifestUrl);
            var manifest = JObject.Parse(json);
            var versions = manifest["versions"] as JArray;

            _allVersions.Clear();

            if (versions != null)
            {
                foreach (var version in versions)
                {
                    var id = version["id"]?.ToString();
                    var type = version["type"]?.ToString() ?? "release";
                    if (string.IsNullOrEmpty(id))
                        continue;

                    var releaseTime = DateTime.MinValue;
                    if (version["releaseTime"] != null &&
                        DateTime.TryParse(version["releaseTime"]!.ToString(), out var parsed))
                    {
                        releaseTime = parsed;
                    }

                    _allVersions.Add(new VersionEntry { Id = id, Type = type, ReleaseTime = releaseTime });
                }

                _allVersions.Sort((a, b) => b.ReleaseTime.CompareTo(a.ReleaseTime));
                PopulateVersionComboBox();
            }
        }
        catch (Exception ex)
        {
            VersionComboBox.ItemsSource = new[] { new VersionListItem { Id = "", Display = LocalizationService.F("add_build.load_versions_error", ex.Message) } };
            VersionComboBox.IsEnabled = false;
            MessageBox.Show(LocalizationService.F("add_build.load_versions_error", ex.Message), LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isLoadingVersions = false;
            RefreshVersionsButton.IsEnabled = true;
            UpdateCreateButtonState();
        }
    }

    private async Task LoadLoaderVersions()
    {
        if (LoaderVersionComboBox == null || LoaderComboBox == null)
            return;

        var mcVersion = (VersionComboBox.SelectedItem as VersionListItem)?.Id;
        var loader = LoaderComboBox.SelectedItem as string;

        if (string.IsNullOrEmpty(mcVersion) || string.IsNullOrEmpty(loader))
            return;

        _loaderVersionsCts?.Cancel();
        _loaderVersionsCts?.Dispose();
        _loaderVersionsCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = _loaderVersionsCts.Token;
        var requestId = ++_loaderVersionsRequestId;
        var requestedLoader = loader;
        var requestedMcVersion = mcVersion;

        LoaderVersionComboBox.IsEnabled = false;
        LoaderVersionComboBox.ItemsSource = new[] { LocalizationService.T("add_build.loading") };

        try
        {
            List<string> versions = requestedLoader.ToLowerInvariant() switch
            {
                "fabric" => await _loaderService.FetchFabricLoaderVersions(requestedMcVersion, token),
                "quilt" => await _loaderService.FetchQuiltLoaderVersions(requestedMcVersion, token),
                "forge" => await _loaderService.FetchForgeVersions(requestedMcVersion, token),
                "neoforge" => await _loaderService.FetchNeoForgeVersions(requestedMcVersion, token),
                _ => new List<string>()
            };

            if (!IsCurrentLoaderRequest(requestId, requestedLoader, requestedMcVersion))
                return;

            if (versions.Count == 0)
            {
                LoaderVersionComboBox.ItemsSource = new[] { LocalizationService.T("add_build.no_loader_versions") };
                LoaderVersionComboBox.IsEnabled = false;
            }
            else
            {
                LoaderVersionComboBox.ItemsSource = versions;
                LoaderVersionComboBox.IsEnabled = true;
                LoaderVersionComboBox.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer loader/MC selection replaced this request.
        }
        catch (Exception ex)
        {
            if (!IsCurrentLoaderRequest(requestId, requestedLoader, requestedMcVersion))
                return;

            LoaderVersionComboBox.ItemsSource = new[] { $"❌ {ex.Message}" };
            LoaderVersionComboBox.IsEnabled = false;
        }
        finally
        {
            if (IsCurrentLoaderRequest(requestId, requestedLoader, requestedMcVersion))
                UpdateCreateButtonState();
        }
    }

    private bool IsCurrentLoaderRequest(int requestId, string loader, string mcVersion)
    {
        if (requestId != _loaderVersionsRequestId)
            return false;

        if (!string.Equals(LoaderComboBox.SelectedItem as string, loader, StringComparison.Ordinal))
            return false;

        return string.Equals((VersionComboBox.SelectedItem as VersionListItem)?.Id, mcVersion, StringComparison.Ordinal);
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (VersionComboBox.SelectedItem is not VersionListItem versionItem || string.IsNullOrEmpty(versionItem.Id))
        {
            MessageBox.Show(LocalizationService.T("add_build.select_mc_version"), LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isModded = IsModdedSelected();
        var version = versionItem.Id;
        var loader = isModded ? LoaderComboBox.SelectedItem as string ?? "Fabric" : "";
        var loaderVersion = isModded ? LoaderVersionComboBox.SelectedItem as string : "";

        if (isModded)
        {
            if (string.IsNullOrEmpty(loaderVersion) || IsLoaderPlaceholder(loaderVersion))
            {
                MessageBox.Show(LocalizationService.T("add_build.select_loader_version"), LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var name = BuildNameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = BuildInfo.GenerateDefaultName(version, loader, loaderVersion ?? "", isModded);
        }

        CreatedBuild = new BuildInfo
        {
            Name = name,
            MinecraftVersion = version,
            Loader = loader,
            LoaderVersion = loaderVersion ?? "",
            IsModded = isModded,
            InstallFabricApi = isModded && loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase)
        };

        RecentMcVersionsHelper.Record(_settings.RecentMcVersions, version);
        _settings.Save();

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
