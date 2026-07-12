using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using System.IO;
using System.Collections.Generic;
using Apeiron.Services;

namespace Apeiron;

public partial class MainWindow : Window, ILocalizable
{
    private readonly MinecraftService _minecraft;
    private readonly JavaService _java;
    private readonly AuthService _auth;
    private readonly BuildManager _buildManager = new();
    private readonly SettingsService _settings;
    private readonly LogService _logService;
    private readonly LoaderService _loader;
    private readonly BuildInstallService _buildInstall;
    private readonly VersionLauncher _versionLauncher;
    private readonly PlayOrchestrator _playOrchestrator;
    private readonly LauncherOrchestrator _launcherOrchestrator;
    private readonly SkinService _skinService;
    private readonly ThemeService _themeService = new();
    private readonly List<string> _mcOutputLines = new();
    private readonly InstallUiCoordinator _installUi = new();
    private string? _lastInstallLogPath;
    private Process? _gameProcess;
    private bool _authInProgress = false;
    private bool _isDownloading = false;
    private bool _isDarkTheme = false;
    private BuildInfo? _currentBuild = null;
    private readonly string? _pendingLaunchTarget;
    private const int MaxConsoleLines = 500;
    
    public MainWindow(SettingsService settings, string? launchTarget = null)
    {
        InitializeComponent();

        _settings = settings;
        _pendingLaunchTarget = launchTarget;
        _logService = new LogService();
        _minecraft = new MinecraftService(_settings.GetMinecraftDir());
        _java = new JavaService();
        _auth = new AuthService();
        _loader = new LoaderService(_minecraft.MinecraftDir);
        _buildInstall = new BuildInstallService(_minecraft, _loader);
        _versionLauncher = new VersionLauncher(_minecraft.MinecraftDir);
        _playOrchestrator = new PlayOrchestrator(_minecraft, _buildInstall, _versionLauncher, _java);
        _launcherOrchestrator = new LauncherOrchestrator(_playOrchestrator, _minecraft);
        _skinService = new SkinService(_minecraft.MinecraftDir);
        
        _auth.OnAuthSuccess += OnAuthSuccess!;
        _auth.OnAuthError += OnAuthError!;
        _auth.OnSessionExpired += OnSessionExpired!;
        
        _buildInstall.ProgressChanged += (p, t) =>
            Dispatcher.Invoke(() => UpdateDownloadProgress(p, t, lockPlayButton: true));

        _buildInstall.Log += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                LogText.Text = msg;
                AddConsoleLine(msg);
            });
        };

        _versionLauncher.Log += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (msg.StartsWith("[MC]"))
                    _mcOutputLines.Add(msg);
                LogText.Text = msg;
                AddConsoleLine(msg);
            });
        };
        
        _minecraft.ProgressChanged += (p, t) =>
            Dispatcher.Invoke(() => UpdateDownloadProgress(p, t, lockPlayButton: true));

        _minecraft.Log += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                LogText.Text = msg;
                AddConsoleLine(msg);
            });
        };
        
        _java.Log += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                LogText.Text = msg;
                AddConsoleLine(msg);
            });
        };
        
        _java.ProgressChanged += (p, t) =>
            Dispatcher.Invoke(() => UpdateDownloadProgress(p, t));

        if (!_settings.OfflineOnly && _auth.LoadSavedAuth())
        {
            var username = _auth.GetUsername();
            var uuid = _auth.GetUUID();
            SetProfileState(true, username);
            AddConsoleLine(LocalizationService.F("main.auth_restored", username ?? ""));
            _ = LoadSkinAsync(uuid);

            _ = Task.Run(async () =>
            {
                if (!await _auth.EnsureValidSessionAsync())
                {
                    Dispatcher.Invoke(() =>
                    {
                        SetProfileState(false);
                        LogText.Text = LocalizationService.T("main.session_expired_status");
                        AddConsoleLine(LocalizationService.T("main.session_expired_console"));
                    });
                }
            });
        }
        else
        {
            SetProfileState(false);
            SetDefaultSkin();
        }
        
        LoadThemeSetting();
        LoadBuilds();
        OfflineNameTextBox.Text = _settings.OfflineUsername;
        ApplyLocalization();

        Loaded += (_, _) =>
        {
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            PromptJavaInstallIfNeeded();
            ApplyOfflineOnlyMode();
            if (_settings.CheckForUpdates && LauncherMetadata.HasUpdateSource)
                _ = CheckForUpdatesOnStartupAsync();
            TryQuickLaunchFromArgs();
        };
        Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void PromptJavaInstallIfNeeded()
    {
        var mcVersion = _currentBuild?.MinecraftVersion ?? "1.21";
        if (_java.IsJavaInstalled(mcVersion))
            return;

        _ = InstallJavaQuietlyAsync(mcVersion);
    }

    private async Task<bool> EnsureJavaForBuildAsync(BuildInfo build)
    {
        if (_java.IsJavaInstalled(build.MinecraftVersion))
            return true;

        return await InstallJavaQuietlyAsync(build.MinecraftVersion);
    }

    private async Task<bool> InstallJavaQuietlyAsync(string mcVersion)
    {
        DownloadProgress.Visibility = Visibility.Visible;
        ProgressText.Visibility = Visibility.Visible;
        LogText.Text = LocalizationService.F("log.java.downloading", JavaVersionHelper.GetPreferredJavaMajor(mcVersion));

        var ok = await _java.InstallJava(mcVersion);

        DownloadProgress.Visibility = Visibility.Collapsed;
        ProgressText.Visibility = Visibility.Collapsed;
        DownloadProgress.Value = 0;
        ProgressText.Text = "";

        if (!ok)
            LogText.Text = LocalizationService.T("main.java_missing_status");

        return ok;
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(ApplyLocalization);

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("main.window_title");
        BuildsHeaderText.Text = LocalizationService.T("main.builds");
        ConsoleHeaderText.Text = LocalizationService.T("main.console");
        AuthButtonText.Text = LocalizationService.T("main.login_microsoft");
        LogoutButtonText.Text = LocalizationService.T("main.logout");
        OfflineNameLabel.Text = LocalizationService.T("main.offline_name");
        if (OfflineNameHintText != null)
            OfflineNameHintText.Text = LocalizationService.T("main.offline_name_hint");
        if (OfflineNameTextBox != null)
            OfflineNameTextBox.ToolTip = LocalizationService.T("main.offline_name_hint");
        UpdateOfflineNameFeedback();
        CancelDownloadButton.Content = LocalizationService.T("main.cancel_download");
        OpenInstallLogButton.Content = LocalizationService.T("main.open_install_log");

        if (_auth.IsAuthenticated())
            SetProfileState(true, _auth.GetUsername());
        else
            SetProfileState(false);

        RefreshStatusText();
        var selectedId = _currentBuild?.Id;
        LoadBuilds();
        if (!string.IsNullOrEmpty(selectedId))
            SelectBuildInCombo(selectedId);

        UpdateThemeTooltip();
    }

    private void RefreshStatusText()
    {
        if (_currentBuild != null)
            CheckSelectedBuildInstalled();
        else if (!_isDownloading)
            LogText.Text = LocalizationService.T("main.ready");
    }

    private void UpdateThemeTooltip()
    {
        ThemeToggle.ToolTip = LocalizationService.T(_isDarkTheme ? "main.theme_light" : "main.theme_dark");
    }
    
    private void SetProfileState(bool isLoggedIn, string? username = null)
    {
        if (_settings.OfflineOnly)
        {
            ApplyOfflineOnlyMode();
            return;
        }

        if (isLoggedIn && !string.IsNullOrWhiteSpace(username))
        {
            UserInfoText.Text = username;
            UserStatusText.Text = LocalizationService.T("main.microsoft_account");
            AuthButton.Visibility = Visibility.Collapsed;
            LogoutButton.Visibility = Visibility.Visible;
            OfflineNameRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            var offlineName = OfflineUsernameHelper.Sanitize(_settings.OfflineUsername);
            UserInfoText.Text = offlineName;
            UserStatusText.Text = LocalizationService.T("main.login_prompt");
            AuthButton.Visibility = Visibility.Visible;
            LogoutButton.Visibility = Visibility.Collapsed;
            OfflineNameRow.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(OfflineNameTextBox.Text))
                OfflineNameTextBox.Text = offlineName;
            UpdateOfflineNameFeedback();
        }
    }

    private void ApplyOfflineOnlyMode()
    {
        if (!_settings.OfflineOnly)
            return;

        if (_auth.IsAuthenticated())
        {
            _auth.Logout();
            SetDefaultSkin();
        }

        var offlineName = OfflineUsernameHelper.Sanitize(_settings.OfflineUsername);
        UserInfoText.Text = offlineName;
        UserStatusText.Text = LocalizationService.T("main.offline_only_mode");
        AuthButton.Visibility = Visibility.Collapsed;
        LogoutButton.Visibility = Visibility.Collapsed;
        OfflineNameRow.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(OfflineNameTextBox.Text))
            OfflineNameTextBox.Text = offlineName;
        UpdateOfflineNameFeedback();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var update = await LauncherUpdateService.CheckForUpdateAsync(LauncherMetadata.GitHubRepository);
            if (update == null)
                return;

            var message = LocalizationService.F(
                "settings.update_available",
                update.LatestVersion,
                LauncherUpdateService.GetCurrentVersion());

            if (MessageBox.Show(
                    message,
                    LocalizationService.T("settings.check_updates_now"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            var zipPath = await LauncherUpdateService.DownloadUpdatePackageAsync(update.DownloadUrl, update.ExpectedSha256);
            var newExe = LauncherUpdateService.ExtractLauncherExecutable(zipPath);
            LauncherUpdateService.ScheduleApplyUpdate(newExe);
            Application.Current.Shutdown();
        }
        catch
        {
            // Silent on startup failures
        }
    }

    private void UpdateOfflineNameFeedback()
    {
        if (OfflineNameErrorText == null || OfflineNameTextBox == null)
            return;

        if (_auth.IsAuthenticated() || OfflineNameRow.Visibility != Visibility.Visible)
        {
            OfflineNameErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        var message = GetOfflineUsernameErrorMessage(OfflineNameTextBox.Text);
        if (string.IsNullOrEmpty(message))
        {
            OfflineNameErrorText.Visibility = Visibility.Collapsed;
            OfflineNameErrorText.Text = "";
            return;
        }

        OfflineNameErrorText.Text = message;
        OfflineNameErrorText.Visibility = Visibility.Visible;
    }

    private static string? GetOfflineUsernameErrorMessage(string? name)
    {
        return OfflineUsernameHelper.Validate(name) switch
        {
            OfflineUsernameValidation.Valid => null,
            OfflineUsernameValidation.Empty => LocalizationService.T("main.offline_name_empty"),
            OfflineUsernameValidation.TooShort => LocalizationService.F("main.offline_name_too_short", OfflineUsernameHelper.MinLength),
            _ => LocalizationService.T("main.offline_name_invalid")
        };
    }

    private bool TryValidateOfflineUsernameForLaunch()
    {
        if (_auth.IsAuthenticated())
            return true;

        var error = GetOfflineUsernameErrorMessage(OfflineNameTextBox.Text);
        if (string.IsNullOrEmpty(error))
            return true;

        UpdateOfflineNameFeedback();
        LogText.Text = error;
        AddConsoleLine("❌ " + error);
        OfflineNameTextBox.Focus();
        return false;
    }

    private void UpdateDownloadProgress(int progress, string text, bool lockPlayButton = false)
    {
        var update = DownloadProgressHelper.CreateUpdate(progress, text);
        DownloadProgress.Visibility = Visibility.Visible;
        ProgressText.Visibility = Visibility.Visible;
        if (update.UpdateBarValue)
            DownloadProgress.Value = update.BarValue;
        if (!string.IsNullOrEmpty(update.StatusText))
            ProgressText.Text = update.StatusText;

        if (lockPlayButton)
        {
            PlayButton.Content = CreateButtonContent("⏳", "main.downloading");
            PlayButton.IsEnabled = false;
        }
    }

    private void SaveOfflineUsername()
    {
        var name = OfflineUsernameHelper.Sanitize(OfflineNameTextBox.Text);
        OfflineNameTextBox.Text = name;
        _settings.OfflineUsername = name;
        _settings.Save();

        if (!_auth.IsAuthenticated())
            UserInfoText.Text = name;

        UpdateOfflineNameFeedback();
    }

    private void OfflineNameTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateOfflineNameFeedback();

    private void OfflineNameTextBox_LostFocus(object sender, RoutedEventArgs e) => SaveOfflineUsername();

    private void OfflineNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SaveOfflineUsername();
    }

    private void OfflineNameTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(ch => !IsOfflineNameCharacterAllowed(ch));
    }

    private void OfflineNameTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pasted = e.DataObject.GetData(DataFormats.Text) as string ?? "";
        if (OfflineUsernameHelper.NormalizeInput(pasted) != pasted.Trim())
            e.CancelCommand();
    }

    private static bool IsOfflineNameCharacterAllowed(char ch) =>
        ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _installUi.CancelDownload();
        AddConsoleLine(LocalizationService.T("main.download_cancelled"));
    }

    private CancellationToken BeginDownloadUi()
    {
        _lastInstallLogPath = null;
        OpenInstallLogButton.Visibility = Visibility.Collapsed;
        CancelDownloadButton.Visibility = Visibility.Visible;
        DownloadProgress.Visibility = Visibility.Visible;
        ProgressText.Visibility = Visibility.Visible;
        return _installUi.BeginDownload();
    }

    private void EndDownloadUi()
    {
        _installUi.EndDownload();
        CancelDownloadButton.Visibility = Visibility.Collapsed;
        DownloadProgress.Visibility = Visibility.Collapsed;
        ProgressText.Visibility = Visibility.Collapsed;
        DownloadProgress.Value = 0;
        ProgressText.Text = "";
    }

    private void HandleInstallFailure(BuildInfo build, bool wasCancelled)
    {
        if (wasCancelled)
            return;

        var logPath = _installUi.SaveInstallFailureLog(build.DisplayName, _logService, wasCancelled);
        if (string.IsNullOrEmpty(logPath))
            return;

        _lastInstallLogPath = logPath;
        OpenInstallLogButton.Visibility = Visibility.Visible;
        LogText.Text = LocalizationService.T("main.install_failed_short");
        AddConsoleLine(LocalizationService.F("main.install_log_saved", Path.GetFileName(logPath)));
    }

    private void OpenInstallLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastInstallLogPath))
            LogService.OpenLogFile(_lastInstallLogPath);
        else
            LogService.OpenLogsFolder(_logService.LogsDirectory);
    }

    private void LoadBuilds()
    {
        try
        {
            var builds = _buildManager.LoadBuilds();
            PopulateBuildsCombo(builds);
        }
        catch (Exception ex)
        {
            AddConsoleLine(LocalizationService.F("main.load_builds_error", ex.Message));
        }
    }

    private void PopulateBuildsCombo(List<BuildInfo> builds, string? selectBuildId = null)
    {
        BuildsComboBox.Items.Clear();

        if (builds == null || builds.Count == 0)
        {
            BuildsComboBox.Items.Add(LocalizationService.T("main.no_builds"));
            BuildsComboBox.IsEnabled = false;
            _currentBuild = null;
            PlayButton.IsEnabled = false;
            SetPlayButton("⚠️", "main.no_builds_short");
            return;
        }

        BuildsComboBox.IsEnabled = true;

        foreach (var build in builds)
        {
            build.IsPrimary = build.Id == _settings.DefaultBuildId;
            BuildsComboBox.Items.Add(build);
        }

        SelectBuildInCombo(selectBuildId ?? _settings.DefaultBuildId);
    }

    private void SelectBuildInCombo(string? buildId)
    {
        var index = 0;

        if (!string.IsNullOrEmpty(buildId))
        {
            for (int i = 0; i < BuildsComboBox.Items.Count; i++)
            {
                if (BuildsComboBox.Items[i] is BuildInfo b && b.Id == buildId)
                {
                    index = i;
                    break;
                }
            }
        }

        BuildsComboBox.SelectedIndex = index;
        _currentBuild = BuildsComboBox.SelectedItem as BuildInfo;
        CheckSelectedBuildInstalled();
    }

    private bool TrySelectBuildByIdOrName(string key)
    {
        var build = BuildListHelper.FindByIdOrName(BuildsComboBox.Items.OfType<BuildInfo>(), key);
        if (build == null)
            return false;

        BuildsComboBox.SelectedItem = build;
        _currentBuild = build;
        CheckSelectedBuildInstalled();
        return true;
    }

    private void TryQuickLaunchFromArgs()
    {
        if (string.IsNullOrWhiteSpace(_pendingLaunchTarget))
            return;

        var target = _pendingLaunchTarget;
        if (!TrySelectBuildByIdOrName(target))
        {
            AddConsoleLine(LocalizationService.F("main.cli_build_not_found", target));
            return;
        }

        AddConsoleLine(LocalizationService.F("main.cli_launch", _currentBuild!.DisplayName));
        UiAsync.Run(PlayButton_ClickAsync, Dispatcher);
    }

    private void BuildsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BuildsComboBox.SelectedItem == null) return;
        
        // Проверяем, не сообщение ли это "Нет сборок"
        if (BuildsComboBox.SelectedItem is string)
            return;
        
        _currentBuild = BuildsComboBox.SelectedItem as BuildInfo;
        
        if (_currentBuild != null)
        {
            CheckSelectedBuildInstalled();
        }
    }
    
    private void CheckSelectedBuildInstalled()
    {
        if (_currentBuild == null) return;

        PlayButton.IsEnabled = true;
        var mode = BuildUiState.GetPlayButtonMode(_currentBuild, _minecraft.MinecraftDir);
        var (icon, key) = BuildUiState.GetPlayButtonContent(mode);
        SetPlayButton(icon, key);
        LogText.Text = mode == PlayButtonMode.Play
            ? LocalizationService.F("main.build_ready", _currentBuild.DisplayName)
            : LocalizationService.F("main.build_download_hint", _currentBuild.DisplayName);
    }

    private void ImportModpack_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = LocalizationService.T("main.import_modpack_tooltip"),
                Filter = LocalizationService.T("edit_build.export_filter")
            };

            if (dialog.ShowDialog() != true)
                return;

            ImportModpackFromZip(dialog.FileName);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AddConsoleLine(LocalizationService.F("main.create_build_error", ex.Message));
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        var zipFiles = FileDropHelper.GetZipFiles(files);
        if (zipFiles.Count == 0)
        {
            AddConsoleLine(LocalizationService.T("main.modpack_drop_invalid"));
            return;
        }

        foreach (var zipPath in zipFiles)
        {
            try
            {
                ImportModpackFromZip(zipPath);
            }
            catch (InvalidOperationException ex)
            {
                AddConsoleLine(ex.Message);
            }
            catch (Exception ex)
            {
                AddConsoleLine(LocalizationService.F("main.create_build_error", ex.Message));
            }
        }
    }

    private void ImportModpackFromZip(string zipPath)
    {
        var instancesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instances");
        Directory.CreateDirectory(instancesRoot);
        var build = ModpackImportService.ImportAsNewInstance(zipPath, instancesRoot);

        _buildManager.AddBuild(build);

        if (BuildsComboBox.Items.Count == 1 && BuildsComboBox.Items[0] is string)
            BuildsComboBox.Items.Clear();

        BuildsComboBox.IsEnabled = true;
        BuildsComboBox.Items.Add(build);
        BuildsComboBox.SelectedIndex = BuildsComboBox.Items.Count - 1;
        _currentBuild = build;

        AddConsoleLine(LocalizationService.F("main.modpack_imported", build.DisplayName));
        if (string.IsNullOrWhiteSpace(build.MinecraftVersion))
        {
            MessageBox.Show(
                LocalizationService.T("main.modpack_configure_hint"),
                LocalizationService.T("main.import_modpack_tooltip"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        CheckSelectedBuildInstalled();
    }

    private void AddBuild_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var addBuildWindow = new AddBuildWindow();
            addBuildWindow.Owner = this;
            
            if (addBuildWindow.ShowDialog() == true && addBuildWindow.CreatedBuild != null)
            {
                var build = addBuildWindow.CreatedBuild;
                
                _buildManager.AddBuild(build);
                
                // Если было сообщение "Нет сборок", удаляем его
                if (BuildsComboBox.Items.Count == 1 && BuildsComboBox.Items[0] is string)
                {
                    BuildsComboBox.Items.Clear();
                }
                
                BuildsComboBox.IsEnabled = true;
                BuildsComboBox.Items.Add(build);
                BuildsComboBox.SelectedIndex = BuildsComboBox.Items.Count - 1;
                _currentBuild = build;
                
                AddConsoleLine(LocalizationService.F("main.build_created", build.DisplayName));
                CheckSelectedBuildInstalled();
            }
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AddConsoleLine(LocalizationService.F("main.create_build_error", ex.Message));
            MessageBox.Show(LocalizationService.F("common.error") + ": " + ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void EditBuild_Click(object sender, RoutedEventArgs e) =>
        UiAsync.Run(EditBuildAsync, Dispatcher);

    private async Task EditBuildAsync()
    {
        if (_currentBuild == null)
        {
            MessageBox.Show(LocalizationService.T("main.select_build_edit"), LocalizationService.T("common.attention"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editWindow = new EditBuildWindow(_currentBuild, _settings);
        editWindow.Owner = this;

        if (editWindow.ShowDialog() != true)
            return;

        try
        {
            if (editWindow.DeleteRequested)
            {
                var deletedId = _currentBuild.Id;
                var deletedName = _currentBuild.Name;
                if (_settings.DefaultBuildId == deletedId)
                {
                    _settings.DefaultBuildId = "";
                    _settings.Save();
                }
                _buildManager.RemoveBuildById(deletedId);
                ReloadBuildsAfterDelete();
                AddConsoleLine(LocalizationService.F("main.build_deleted", deletedName));
                return;
            }

            if (editWindow.DuplicateRequested)
            {
                var sourceId = _currentBuild.Id;
                var clone = _buildManager.DuplicateBuild(sourceId, copyModsAndConfig: true);
                var builds = _buildManager.LoadBuilds();
                PopulateBuildsCombo(builds, clone.Id);
                AddConsoleLine(LocalizationService.F("main.build_copied", clone.DisplayName));
                return;
            }

            if (editWindow.SavedBuild != null)
            {
                _buildManager.UpdateBuild(editWindow.SavedBuild);
                var builds = _buildManager.LoadBuilds();
                PopulateBuildsCombo(builds, editWindow.SavedBuild.Id);
                AddConsoleLine(LocalizationService.F("main.build_updated", editWindow.SavedBuild.DisplayName));
            }

            if (editWindow.ReinstallRequested)
                await ReinstallBuildAsync();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AddConsoleLine($"❌ {ex.Message}");
        }
    }

    private void ReloadBuildsAfterDelete()
    {
        var builds = _buildManager.LoadBuilds();
        PopulateBuildsCombo(builds);
    }

    private async Task ReinstallBuildAsync()
    {
        if (_currentBuild == null) return;

        _isDownloading = true;
        PlayButton.IsEnabled = false;
        PlayButton.Content = CreateButtonContent("⏳", "main.reinstalling");
        LogText.Text = LocalizationService.T("main.reinstall_status");
        AddConsoleLine(LocalizationService.F("main.reinstall_console", _currentBuild.DisplayName));

        try
        {
            var removed = _launcherOrchestrator.ClearForReinstall(_currentBuild);
            foreach (var id in removed)
                AddConsoleLine(LocalizationService.F(
                    id == _currentBuild.GetVersionId() ? "main.profile_deleted" : "main.base_deleted",
                    id));

            PlayButton.Content = CreateButtonContent("⏳", "main.downloading");
            var cancellationToken = BeginDownloadUi();
            var installResult = await _launcherOrchestrator.ReinstallAsync(_currentBuild, cancellationToken);
            var wasCancelled = installResult == InstallFlowResult.Cancelled;
            EndDownloadUi();

            if (installResult is InstallFlowResult.Success or InstallFlowResult.AlreadyInstalled)
            {
                PlayButton.IsEnabled = true;
                PlayButton.Content = CreateButtonContent("▶", "main.play");
                LogText.Text = LocalizationService.F("main.reinstall_done", _currentBuild.DisplayName);
                AddConsoleLine(LocalizationService.F("main.reinstall_done", _currentBuild.DisplayName));
            }
            else
            {
                LogText.Text = wasCancelled
                    ? LocalizationService.T("main.download_cancelled")
                    : LocalizationService.F("main.reinstall_failed", _currentBuild.DisplayName);
                if (!wasCancelled)
                    HandleInstallFailure(_currentBuild, wasCancelled);
                PlayButton.IsEnabled = true;
                PlayButton.Content = CreateButtonContent("⬇️", "main.download");
            }
        }
        catch (Exception ex)
        {
            LogText.Text = LocalizationService.F("main.error_with_message", ex.Message);
            AddConsoleLine(LocalizationService.F("main.error_with_message", ex.Message));
            if (_currentBuild != null)
                HandleInstallFailure(_currentBuild, wasCancelled: false);
            PlayButton.IsEnabled = true;
            PlayButton.Content = CreateButtonContent("▶", "main.play");
        }
        finally
        {
            _isDownloading = false;
            CheckSelectedBuildInstalled();
        }
    }

    private void ClearConsole_Click(object sender, RoutedEventArgs e)
    {
        if (ConsoleRichTextBox == null || ConsoleRichTextBox.Document == null) return;
        
        ConsoleRichTextBox.Document.Blocks.Clear();
        ConsoleRichTextBox.Document.Blocks.Add(new Paragraph(new Run("")));
        AddConsoleLine(LocalizationService.T("main.console_cleared"));
    }
    
    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme();
        UpdateConsoleColors();

        _settings.DarkTheme = _isDarkTheme;
        _settings.Save();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings, _logService) { Owner = this };
        if (window.ShowDialog() != true || !window.Saved)
            return;

        ApplyLocalization();

        ApplyOfflineOnlyMode();
        AddConsoleLine(LocalizationService.T("main.settings_saved"));
    }

    private void ApplyTheme()
    {
        _themeService.Apply(_isDarkTheme);

        Background = _themeService.WindowBackgroundBrush;
        TitleBar.Background = _themeService.SurfaceBrush;
        TitleBar.BorderBrush = _themeService.BorderBrush;
        LeftPanel.Background = _themeService.SurfaceBrush;
        LeftPanel.BorderBrush = _themeService.BorderBrush;
        RightPanel.Background = _themeService.SurfaceBrush;
        RightPanel.BorderBrush = _themeService.BorderBrush;
        StatusBorder.Background = _themeService.SurfaceAltBrush;
        StatusBorder.BorderBrush = _themeService.BorderBrush;
        LogText.Foreground = _themeService.StatusBrush;
        ConsoleBorder.Background = _themeService.IsDark ? _themeService.WindowBackgroundBrush : _themeService.SurfaceAltBrush;
        ConsoleBorder.BorderBrush = _themeService.BorderBrush;
        ConsoleRichTextBox.Background = _themeService.IsDark ? _themeService.WindowBackgroundBrush : _themeService.SurfaceAltBrush;
        ConsoleRichTextBox.Foreground = _themeService.ConsoleBrush;
        ProfileBorder.Background = _themeService.SurfaceAltBrush;
        ProfileBorder.BorderBrush = _themeService.BorderBrush;
        SkinBorder.Background = _themeService.BorderBrush;
        UserInfoText.Foreground = _themeService.PrimaryTextBrush;
        UserStatusText.Foreground = _themeService.SecondaryTextBrush;
        AuthButton.Foreground = Brushes.White;
        PlayButton.Foreground = Brushes.White;
        DownloadProgress.Foreground = _themeService.ProgressForegroundBrush;
        DownloadProgress.Background = _themeService.ProgressBackgroundBrush;
        ProgressText.Foreground = _themeService.SecondaryTextBrush;
        ThemeIcon.Text = _themeService.ThemeIcon;
        ThemeToggle.ToolTip = LocalizationService.T(_themeService.ThemeTooltipKey);
    }

    private void LoadThemeSetting()
    {
        _isDarkTheme = _settings.DarkTheme;
        ApplyTheme();
        UpdateConsoleColors();
    }
    
    private void UpdateConsoleColors()
    {
        if (ConsoleRichTextBox == null || ConsoleRichTextBox.Document == null) return;
        
        var brush = _themeService.ConsoleBrush;
        ConsoleRichTextBox.Foreground = brush;
        
        var document = ConsoleRichTextBox.Document;
        if (document != null)
        {
            foreach (var block in document.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    foreach (var inline in paragraph.Inlines)
                    {
                        if (inline is Run run)
                        {
                            run.Foreground = brush;
                        }
                    }
                }
            }
        }
    }
    
    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    
    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            this.DragMove();
        }
    }
    
    private void OnAuthSuccess(string username, string uuid)
    {
        Dispatcher.Invoke(() =>
        {
            _authInProgress = false;
            SetProfileState(true, username);
            LogText.Text = LocalizationService.F("main.auth_success_status", username);
            AddConsoleLine(LocalizationService.F("main.auth_welcome", username));
            AddConsoleLine(LocalizationService.F("main.auth_uuid", uuid));
            _ = LoadSkinAsync(uuid);
        });
    }
    
    private void OnAuthError(string error)
    {
        Dispatcher.Invoke(() =>
        {
            _authInProgress = false;
            LogText.Text = LocalizationService.F("main.error_with_message", error);
            AddConsoleLine(LocalizationService.F("main.error_with_message", error));
            AuthButton.IsEnabled = true;
        });
    }

    private void OnSessionExpired()
    {
        Dispatcher.Invoke(() =>
        {
            SetProfileState(false);
            SetDefaultSkin();
            LogText.Text = LocalizationService.T("main.session_expired_status");
            AddConsoleLine(LocalizationService.T("main.session_expired_console"));
        });
    }
    
    private void AuthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.OfflineOnly || _authInProgress) return;
        AuthButton.IsEnabled = false;
        _authInProgress = true;
        LogText.Text = LocalizationService.T("main.auth_starting_status");
        AddConsoleLine(LocalizationService.T("main.auth_starting_console"));
        
        try
        {
            var authWindow = new AuthWindow(this, _auth);
            authWindow.Closed += (s, args) =>
            {
                _authInProgress = false;
                AuthButton.IsEnabled = true;
                AddConsoleLine(LocalizationService.T("main.auth_window_closed"));
            };
            authWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            AddConsoleLine(LocalizationService.F("main.error_with_message", ex.Message));
            AuthButton.IsEnabled = true;
            _authInProgress = false;
        }
    }
    
    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _auth.Logout();
        SetProfileState(false);
        LogText.Text = LocalizationService.T("main.logout_done");
        AddConsoleLine(LocalizationService.T("main.logout_console"));
        _authInProgress = false;
        SetDefaultSkin();
    }
    
    private void AddConsoleLine(string message)
    {
        if (ConsoleRichTextBox == null || ConsoleRichTextBox.Document == null)
        {
            return;
        }

        try
        {
            var paragraph = new Paragraph();
            paragraph.Margin = new Thickness(0, 0, 0, 0);
            
            var run = new Run($"{DateTime.Now:HH:mm:ss} > {message}\n");
            run.Foreground = _isDarkTheme ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.DarkGreen);
            paragraph.Inlines.Add(run);
            ConsoleRichTextBox.Document.Blocks.Add(paragraph);

            if (_installUi.IsActive)
                _installUi.RecordLogLine(message);

            while (ConsoleRichTextBox.Document.Blocks.Count > MaxConsoleLines)
                ConsoleRichTextBox.Document.Blocks.Remove(ConsoleRichTextBox.Document.Blocks.FirstBlock);
            
            var scrollViewer = FindParent<ScrollViewer>(ConsoleRichTextBox);
            scrollViewer?.ScrollToEnd();

            if (ShouldWriteToSessionLog(message))
                _logService.WriteLine(message);
        }
        catch
        {
            // Игнорируем ошибки консоли
        }
    }

    private static bool ShouldWriteToSessionLog(string message) =>
        !message.StartsWith("[Installer]", StringComparison.Ordinal);
    
    private T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null && parent is not T)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }
        return parent as T;
    }
    
    private void SetPlayButton(string icon, string textKey)
    {
        PlayButtonIcon.Text = icon + (icon.Length > 1 ? "" : " ");
        PlayButtonLabel.Text = LocalizationService.T(textKey);
    }

    private StackPanel CreateButtonContent(string icon, string textKey)
    {
        SetPlayButton(icon, textKey);
        return (StackPanel)PlayButton.Content;
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) =>
        UiAsync.Run(PlayButton_ClickAsync, Dispatcher);

    private async Task PlayButton_ClickAsync()
    {
        if (_isDownloading) return;
        PlayButton.IsEnabled = false;
        _isDownloading = true;

        try
        {
            var validation = _launcherOrchestrator.ValidateBuild(_currentBuild);
            if (validation == PlayValidationResult.NoBuild)
            {
                MessageBox.Show(LocalizationService.T("main.select_build_launch"), LocalizationService.T("main.launch_title"), MessageBoxButton.OK, MessageBoxImage.Information);
                LogText.Text = LocalizationService.T("main.no_build_selected_log");
                RestorePlayButton();
                return;
            }

            if (validation == PlayValidationResult.UnsupportedLoader)
            {
                MessageBox.Show(
                    LocalizationService.F("main.loader_unsupported", _currentBuild!.Loader),
                    LocalizationService.T("main.launch_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                RestorePlayButton();
                return;
            }

            if (!TryValidateOfflineUsernameForLaunch())
            {
                RestorePlayButton();
                return;
            }

            if (!await EnsureJavaForBuildAsync(_currentBuild!))
            {
                RestorePlayButton();
                return;
            }

            if (!_launcherOrchestrator.IsInstalled(_currentBuild!))
            {
                PlayButton.Content = CreateButtonContent("⏳", "main.downloading");
                var cancellationToken = BeginDownloadUi();
                var installResult = await _launcherOrchestrator.InstallIfNeededAsync(_currentBuild!, cancellationToken);
                var wasCancelled = installResult == InstallFlowResult.Cancelled;
                EndDownloadUi();
                if (installResult is InstallFlowResult.Failed or InstallFlowResult.Cancelled)
                {
                    LogText.Text = wasCancelled
                        ? LocalizationService.T("main.download_cancelled")
                        : LocalizationService.T("main.install_failed_short");
                    if (!wasCancelled)
                        HandleInstallFailure(_currentBuild!, wasCancelled);
                    PlayButton.IsEnabled = true;
                    PlayButton.Content = CreateButtonContent("⬇️", "main.download");
                    _isDownloading = false;
                    return;
                }
            }

            PlayButton.Content = CreateButtonContent("⏳", "main.launching");

            var preparation = await _launcherOrchestrator.PrepareLaunchAsync(
                _currentBuild!,
                _auth,
                _settings,
                OfflineNameTextBox.Text);

            if (preparation.Result == LaunchPreparationResult.JavaMissing)
            {
                MessageBox.Show(
                    LocalizationService.T("main.java_not_found_launch"),
                    LocalizationService.T("main.java_not_found_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                RestorePlayButton();
                return;
            }

            if (preparation.Result == LaunchPreparationResult.SessionExpired)
            {
                MessageBox.Show(
                    LocalizationService.T("main.session_expired"),
                    LocalizationService.T("main.session_expired_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                LogText.Text = LocalizationService.T("main.session_expired_status");
                AddConsoleLine(LocalizationService.T("main.session_expired_console_short"));
                RestorePlayButton();
                return;
            }

            var identity = preparation.Identity;
            var ram = _currentBuild!.ResolveRamGb(_settings.Ram);
            if (identity.IsOffline)
                AddConsoleLine(LocalizationService.F("main.launch_offline", ram, _currentBuild.DisplayName));
            else
                AddConsoleLine(LocalizationService.F("main.launch_online", identity.Username, ram, _currentBuild.DisplayName));

            _mcOutputLines.Clear();
            _gameProcess = await _launcherOrchestrator.LaunchGameAsync(_currentBuild!, identity, _settings.Ram);

            if (_gameProcess != null)
            {
                LogText.Text = LocalizationService.T("main.game_launched");
                AddConsoleLine(LocalizationService.T("main.game_launched_console"));
                PlayButton.Content = CreateButtonContent("▶", "main.game_running");
                PlayButton.IsEnabled = false;
                var buildName = _currentBuild.DisplayName;
                _ = Task.Run(() =>
                {
                    _gameProcess.WaitForExit();
                    var exitCode = _gameProcess.ExitCode;
                    Dispatcher.Invoke(() =>
                    {
                        PlayButton.IsEnabled = true;
                        PlayButton.Content = CreateButtonContent("▶", "main.play");
                        _isDownloading = false;

                        if (exitCode != 0)
                        {
                            var logPath = _logService.SaveGameLog(buildName, _mcOutputLines);
                            LogText.Text = LocalizationService.F("main.game_exit_code", exitCode);
                            AddConsoleLine(LocalizationService.F("main.game_exit_code", exitCode));
                            if (!string.IsNullOrEmpty(logPath))
                                AddConsoleLine(LocalizationService.F("main.log_saved", Path.GetFileName(logPath)));
                        }
                        else
                        {
                            LogText.Text = LocalizationService.T("main.game_closed");
                            AddConsoleLine(LocalizationService.T("main.game_closed"));
                        }
                    });
                });
            }
            else
            {
                RestorePlayButton();
            }
        }
        catch (Exception ex)
        {
            LogText.Text = LocalizationService.F("main.error_with_message", ex.Message);
            AddConsoleLine(LocalizationService.F("main.error_with_message", ex.Message));
            RestorePlayButton();
        }
    }

    private void RestorePlayButton()
    {
        EndDownloadUi();
        PlayButton.IsEnabled = true;
        var (icon, key) = BuildUiState.GetPlayButtonContent(
            BuildUiState.GetPlayButtonMode(_currentBuild, _minecraft.MinecraftDir));
        PlayButton.Content = CreateButtonContent(icon, key);
        _isDownloading = false;
    }

    private async Task LoadSkinAsync(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid))
        {
            SetDefaultSkin();
            return;
        }

        SkinBorder.Background = new SolidColorBrush(Colors.LightGray);

        var image = await _skinService.LoadSkinAsync(uuid);
        if (image != null)
        {
            SkinImage.Source = image;
            SkinBorder.Background = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            SetDefaultSkin();
        }
    }

    private void SetDefaultSkin()
    {
        Dispatcher.Invoke(() =>
        {
            SkinBorder.Background = new SolidColorBrush(Colors.LightGray);
            SkinImage.Source = null;
        });
    }
}