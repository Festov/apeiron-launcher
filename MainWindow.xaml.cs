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
using Apeiron.ViewModels;

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
    private bool _suppressAccountComboEvent;
    private readonly MainViewModel _viewModel = new();
    private readonly string? _pendingLaunchTarget;
    private const int MaxConsoleLines = 500;
    
    public MainWindow(SettingsService settings, string? launchTarget = null)
    {
        InitializeComponent();
        DataContext = _viewModel;

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
        _auth.OnAccountsChanged += () => Dispatcher.Invoke(() =>
        {
            RefreshAccountSwitcher();
            if (_auth.IsAuthenticated())
                SetProfileState(true, _auth.GetUsername());
            else if (!_settings.OfflineOnly)
                SetProfileState(false);
        });
        
        _buildInstall.ProgressChanged += (p, t) =>
            Dispatcher.Invoke(() => UpdateDownloadProgress(p, t, lockPlayButton: true));

        _buildInstall.Log += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.SetStatus(msg);
                AddConsoleLine(msg);
            });
        };

        _versionLauncher.Log += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (msg.StartsWith("[MC]"))
                    _mcOutputLines.Add(msg);
                _viewModel.SetStatus(msg);
                AddConsoleLine(msg);
            });
        };
        
        _minecraft.ProgressChanged += (p, t) =>
            Dispatcher.Invoke(() => UpdateDownloadProgress(p, t, lockPlayButton: true));

        _minecraft.Log += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.SetStatus(msg);
                AddConsoleLine(msg);
            });
        };
        
        _java.Log += (msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.SetStatus(msg);
                AddConsoleLine(msg);
            });
        };
        
        _java.ProgressChanged += (p, t) =>
            Dispatcher.Invoke(() => UpdateDownloadProgress(p, t));

        if (!_settings.OfflineOnly && _auth.LoadSavedAuth())
        {
            var username = _auth.GetUsername();
            SetProfileState(true, username);
            AddConsoleLine(LocalizationService.F("main.auth_restored", username ?? ""));
            ApplyLocalSkinAvatar();

            _ = Task.Run(async () =>
            {
                if (!await _auth.EnsureValidSessionAsync())
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SetProfileState(false);
                        _viewModel.SetStatus(LocalizationService.T("main.session_expired_status"));
                        AddConsoleLine(LocalizationService.T("main.session_expired_console"));
                        _ = RefreshAvatarAsync();
                    });
                    return;
                }

                await RefreshAvatarAsync();
            });
        }
        else
        {
            SetProfileState(false);
            _ = RefreshAvatarAsync();
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
        var mcVersion = _viewModel.CurrentBuild?.MinecraftVersion ?? "1.21";
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
        _viewModel.ShowProgressPanel();
        _viewModel.SetStatus(LocalizationService.F("log.java.downloading", JavaVersionHelper.GetPreferredJavaMajor(mcVersion)));

        var ok = await _java.InstallJava(mcVersion);

        _viewModel.HideProgressPanel();

        if (!ok)
            _viewModel.SetStatus(LocalizationService.T("main.java_missing_status"));

        return ok;
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(ApplyLocalization);

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("main.window_title");
        BuildsHeaderText.Text = LocalizationService.T("main.builds");
        ConsoleHeaderText.Text = LocalizationService.T("main.console");
        AuthButtonText.Text = _auth.IsAuthenticated()
            ? LocalizationService.T("main.add_account")
            : LocalizationService.T("main.login_microsoft");
        LogoutButtonText.Text = LocalizationService.T("main.logout");
        OfflineNameLabel.Text = LocalizationService.T("main.offline_name");
        if (OfflineNameHintText != null)
            OfflineNameHintText.Text = LocalizationService.T("main.offline_name_hint");
        if (OfflineNameTextBox != null)
            OfflineNameTextBox.ToolTip = LocalizationService.T("main.offline_name_hint");
        UpdateOfflineNameFeedback();
        CancelDownloadButton.Content = LocalizationService.T("main.cancel_download");
        OpenInstallLogButton.Content = LocalizationService.T("main.open_install_log");
        AddBuildButton.Content = LocalizationService.T("main.add_build_button");
        ImportModpackButton.Content = LocalizationService.T("main.import_modpack_button");
        EditBuildButton.Content = LocalizationService.T("main.edit_build_button");

        if (_auth.IsAuthenticated())
            SetProfileState(true, _auth.GetUsername());
        else
            SetProfileState(false);

        RefreshAccountSwitcher();
        RefreshStatusText();
        var selectedId = _viewModel.CurrentBuild?.Id;
        LoadBuilds();
        if (!string.IsNullOrEmpty(selectedId))
            SelectBuildInCombo(selectedId);

    }

    private void RefreshStatusText() =>
        _viewModel.RefreshLocalizedText(_minecraft.MinecraftDir);

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
            AuthButton.Visibility = Visibility.Visible;
            AuthButtonText.Text = LocalizationService.T("main.add_account");
            LogoutButton.Visibility = Visibility.Visible;
            OfflineNameRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            var offlineName = OfflineUsernameHelper.Sanitize(_settings.OfflineUsername);
            UserInfoText.Text = offlineName;
            UserStatusText.Text = LocalizationService.T("main.login_prompt");
            AuthButton.Visibility = Visibility.Visible;
            AuthButtonText.Text = LocalizationService.T("main.login_microsoft");
            LogoutButton.Visibility = Visibility.Collapsed;
            OfflineNameRow.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(OfflineNameTextBox.Text))
                OfflineNameTextBox.Text = offlineName;
            UpdateOfflineNameFeedback();
        }

        RefreshAccountSwitcher();
    }

    private void RefreshAccountSwitcher()
    {
        if (AccountComboBox == null)
            return;

        if (_settings.OfflineOnly)
        {
            AccountComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        var accounts = _auth.GetAccounts();
        _suppressAccountComboEvent = true;
        try
        {
            AccountComboBox.ItemsSource = accounts;
            AccountComboBox.DisplayMemberPath = nameof(AccountSummary.Username);
            AccountComboBox.SelectedValuePath = nameof(AccountSummary.Uuid);

            if (accounts.Count == 0)
            {
                AccountComboBox.Visibility = Visibility.Collapsed;
                AccountComboBox.SelectedItem = null;
                return;
            }

            AccountComboBox.Visibility = Visibility.Visible;
            var active = accounts.FirstOrDefault(a => a.IsActive) ?? accounts[0];
            AccountComboBox.SelectedItem = accounts.FirstOrDefault(a =>
                string.Equals(a.Uuid, active.Uuid, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressAccountComboEvent = false;
        }
    }

    private void AccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAccountComboEvent || AccountComboBox.SelectedItem is not AccountSummary selected)
            return;

        var current = _auth.GetUUID();
        if (string.Equals(
                (current ?? "").Replace("-", "", StringComparison.Ordinal),
                (selected.Uuid ?? "").Replace("-", "", StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase))
            return;

        if (!_auth.SwitchAccount(selected.Uuid))
            return;

        AddConsoleLine(LocalizationService.F("main.account_switched", selected.Username));
        SetProfileState(true, selected.Username);
        _ = RefreshAvatarAsync();
    }

    private void ApplyOfflineOnlyMode()
    {
        if (!_settings.OfflineOnly)
            return;

        if (_auth.IsAuthenticated() || _auth.GetAccounts().Count > 0)
        {
            _auth.LogoutAll();
            _ = RefreshAvatarAsync();
        }

        var offlineName = OfflineUsernameHelper.Sanitize(_settings.OfflineUsername);
        UserInfoText.Text = offlineName;
        UserStatusText.Text = LocalizationService.T("main.offline_only_mode");
        AuthButton.Visibility = Visibility.Collapsed;
        LogoutButton.Visibility = Visibility.Collapsed;
        if (AccountComboBox != null)
            AccountComboBox.Visibility = Visibility.Collapsed;
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

            var message = LauncherUpdatePromptHelper.BuildPrompt(
                update,
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
        _viewModel.SetStatus(error);
        AddConsoleLine("❌ " + error);
        OfflineNameTextBox.Focus();
        return false;
    }

    private void UpdateDownloadProgress(int progress, string text, bool lockPlayButton = false)
    {
        _viewModel.ApplyDownloadProgress(progress, text);

        if (lockPlayButton)
            _viewModel.SetTransientPlayButton("main.downloading", enabled: false);
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
        _viewModel.BeginDownloadUi();
        return _installUi.BeginDownload();
    }

    private void EndDownloadUi()
    {
        _installUi.EndDownload();
        _viewModel.EndDownloadUi();
    }

    private void HandleInstallFailure(BuildInfo build, bool wasCancelled)
    {
        if (wasCancelled)
            return;

        var logPath = _installUi.SaveInstallFailureLog(build.DisplayName, _logService, wasCancelled);
        if (string.IsNullOrEmpty(logPath))
            return;

        _lastInstallLogPath = logPath;
        _viewModel.ShowOpenInstallLog();
        _viewModel.SetStatus(LocalizationService.T("main.install_failed_short"));
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
            _viewModel.CurrentBuild = null;
            _viewModel.SetTransientPlayButton("main.no_builds_short", enabled: false);
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
        _viewModel.CurrentBuild = BuildsComboBox.SelectedItem as BuildInfo;
        CheckSelectedBuildInstalled();
    }

    private bool TrySelectBuildByIdOrName(string key)
    {
        var build = BuildListHelper.FindByIdOrName(BuildsComboBox.Items.OfType<BuildInfo>(), key);
        if (build == null)
            return false;

        BuildsComboBox.SelectedItem = build;
        _viewModel.CurrentBuild = build;
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

        AddConsoleLine(LocalizationService.F("main.cli_launch", _viewModel.CurrentBuild!.DisplayName));
        UiAsync.Run(PlayButton_ClickAsync, Dispatcher);
    }

    private void BuildsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BuildsComboBox.SelectedItem == null) return;
        
        // Проверяем, не сообщение ли это "Нет сборок"
        if (BuildsComboBox.SelectedItem is string)
            return;
        
        _viewModel.CurrentBuild = BuildsComboBox.SelectedItem as BuildInfo;
        
        if (_viewModel.CurrentBuild != null)
        {
            CheckSelectedBuildInstalled();
        }
    }
    
    private void CheckSelectedBuildInstalled()
    {
        if (_viewModel.CurrentBuild == null) return;
        _viewModel.RefreshPlayState(_minecraft.MinecraftDir);
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
        _viewModel.CurrentBuild = build;

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
            var addBuildWindow = new AddBuildWindow(_settings) { Owner = this };
            using (DialogShade.Begin(this))
            {
                if (addBuildWindow.ShowDialog() == true && addBuildWindow.CreatedBuild != null)
                {
                    var build = addBuildWindow.CreatedBuild;

                    _buildManager.AddBuild(build);

                    if (BuildsComboBox.Items.Count == 1 && BuildsComboBox.Items[0] is string)
                        BuildsComboBox.Items.Clear();

                    BuildsComboBox.IsEnabled = true;
                    BuildsComboBox.Items.Add(build);
                    BuildsComboBox.SelectedIndex = BuildsComboBox.Items.Count - 1;
                    _viewModel.CurrentBuild = build;

                    AddConsoleLine(LocalizationService.F("main.build_created", build.DisplayName));
                    CheckSelectedBuildInstalled();
                }
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
        if (_viewModel.CurrentBuild == null)
        {
            MessageBox.Show(LocalizationService.T("main.select_build_edit"), LocalizationService.T("common.attention"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editWindow = new EditBuildWindow(_viewModel.CurrentBuild, _settings) { Owner = this };

        using (DialogShade.Begin(this))
        {
            if (editWindow.ShowDialog() != true)
                return;
        }

        try
        {
            if (editWindow.DeleteRequested)
            {
                var deletedId = _viewModel.CurrentBuild.Id;
                var deletedName = _viewModel.CurrentBuild.Name;
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
                var sourceId = _viewModel.CurrentBuild.Id;
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
        if (_viewModel.CurrentBuild == null) return;

        _viewModel.IsDownloading = true;
        _viewModel.SetTransientPlayButton("main.reinstalling", enabled: false);
        _viewModel.SetStatus(LocalizationService.T("main.reinstall_status"));
        AddConsoleLine(LocalizationService.F("main.reinstall_console", _viewModel.CurrentBuild.DisplayName));

        try
        {
            var removed = _launcherOrchestrator.ClearForReinstall(_viewModel.CurrentBuild);
            foreach (var id in removed)
                AddConsoleLine(LocalizationService.F(
                    id == _viewModel.CurrentBuild.GetVersionId() ? "main.profile_deleted" : "main.base_deleted",
                    id));

            _viewModel.SetTransientPlayButton("main.downloading", enabled: false);
            var cancellationToken = BeginDownloadUi();
            var installResult = await _launcherOrchestrator.ReinstallAsync(_viewModel.CurrentBuild, cancellationToken);
            var wasCancelled = installResult == InstallFlowResult.Cancelled;
            EndDownloadUi();

            if (installResult is InstallFlowResult.Success or InstallFlowResult.AlreadyInstalled)
            {
                _viewModel.SetStatus(LocalizationService.F("main.reinstall_done", _viewModel.CurrentBuild.DisplayName));
                AddConsoleLine(LocalizationService.F("main.reinstall_done", _viewModel.CurrentBuild.DisplayName));
            }
            else
            {
                _viewModel.SetStatus(wasCancelled
                    ? LocalizationService.T("main.download_cancelled")
                    : LocalizationService.F("main.reinstall_failed", _viewModel.CurrentBuild.DisplayName));
                if (!wasCancelled)
                    HandleInstallFailure(_viewModel.CurrentBuild, wasCancelled);
                _viewModel.SetTransientPlayButton("main.download");
            }
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus(LocalizationService.F("main.error_with_message", ex.Message));
            AddConsoleLine(LocalizationService.F("main.error_with_message", ex.Message));
            if (_viewModel.CurrentBuild != null)
                HandleInstallFailure(_viewModel.CurrentBuild, wasCancelled: false);
        }
        finally
        {
            _viewModel.IsDownloading = false;
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
    
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings, _logService) { Owner = this };
        using (DialogShade.Begin(this))
        {
            if (window.ShowDialog() != true || !window.Saved)
                return;
        }

        ApplyLocalization();

        ApplyOfflineOnlyMode();
        AddConsoleLine(LocalizationService.T("main.settings_saved"));
    }

    private void ApplyTheme()
    {
        _themeService.Apply();

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
        ConsoleBorder.Background = _themeService.ConsoleBackgroundBrush;
        ConsoleBorder.BorderBrush = _themeService.BorderBrush;
        ConsoleRichTextBox.Background = Brushes.Transparent;
        ConsoleRichTextBox.Foreground = _themeService.ConsoleBrush;
        ProfileBorder.Background = _themeService.SurfaceAltBrush;
        ProfileBorder.BorderBrush = _themeService.BorderBrush;
        SkinBorder.Background = _themeService.SurfaceRaisedBrush;
        UserInfoText.Foreground = _themeService.PrimaryTextBrush;
        UserStatusText.Foreground = _themeService.SecondaryTextBrush;
        DownloadProgress.Foreground = _themeService.ProgressForegroundBrush;
        DownloadProgress.Background = _themeService.ProgressBackgroundBrush;
    }

    private void LoadThemeSetting()
    {
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
        if (e.ClickCount == 2)
        {
            MaximizeWindow_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
    
    private void OnAuthSuccess(string username, string uuid)
    {
        Dispatcher.Invoke(() =>
        {
            _authInProgress = false;
            SetProfileState(true, username);
            _viewModel.SetStatus(LocalizationService.F("main.auth_success_status", username));
            AddConsoleLine(LocalizationService.F("main.auth_welcome", username));
            AddConsoleLine(LocalizationService.F("main.auth_uuid", uuid));
            _ = RefreshAvatarAsync();
        });
    }
    
    private void OnAuthError(string error)
    {
        Dispatcher.Invoke(() =>
        {
            _authInProgress = false;
            _viewModel.SetStatus(LocalizationService.F("main.error_with_message", error));
            AddConsoleLine(LocalizationService.F("main.error_with_message", error));
            AuthButton.IsEnabled = true;
            RefreshAccountSwitcher();
        });
    }

    private void OnSessionExpired()
    {
        Dispatcher.Invoke(() =>
        {
            SetProfileState(false);
            _ = RefreshAvatarAsync();
            _viewModel.SetStatus(LocalizationService.T("main.session_expired_status"));
            AddConsoleLine(LocalizationService.T("main.session_expired_console"));
        });
    }
    
    private void AuthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.OfflineOnly || _authInProgress) return;
        AuthButton.IsEnabled = false;
        _authInProgress = true;
        _viewModel.SetStatus(_auth.IsAuthenticated()
            ? LocalizationService.T("main.add_account_starting")
            : LocalizationService.T("main.auth_starting_status"));
        AddConsoleLine(_auth.IsAuthenticated()
            ? LocalizationService.T("main.add_account_starting")
            : LocalizationService.T("main.auth_starting_console"));
        
        try
        {
            var authWindow = new AuthWindow(this, _auth);
            authWindow.Closed += (s, args) =>
            {
                _authInProgress = false;
                AuthButton.IsEnabled = true;
                AddConsoleLine(LocalizationService.T("main.auth_window_closed"));
                RefreshAccountSwitcher();
            };
            using (DialogShade.Begin(this))
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
        var leaving = _auth.GetUsername() ?? "";
        _auth.Logout();

        if (_auth.IsAuthenticated())
        {
            var next = _auth.GetUsername() ?? "";
            SetProfileState(true, next);
            _viewModel.SetStatus(LocalizationService.F("main.account_switched", next));
            AddConsoleLine(LocalizationService.F("main.account_signed_out_switched", leaving, next));
            _ = RefreshAvatarAsync();
        }
        else
        {
            SetProfileState(false);
            _viewModel.SetStatus(LocalizationService.T("main.logout_done"));
            AddConsoleLine(LocalizationService.T("main.logout_console"));
            _ = RefreshAvatarAsync();
        }

        _authInProgress = false;
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
            run.Foreground = _themeService.ConsoleBrush;
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
    
    private void PlayButton_Click(object sender, RoutedEventArgs e) =>
        UiAsync.Run(PlayButton_ClickAsync, Dispatcher);

    private async Task PlayButton_ClickAsync()
    {
        if (_viewModel.IsDownloading) return;
        _viewModel.SetPlayEnabled(false);
        _viewModel.IsDownloading = true;

        try
        {
            var validation = _launcherOrchestrator.ValidateBuild(_viewModel.CurrentBuild);
            if (validation == PlayValidationResult.NoBuild)
            {
                MessageBox.Show(LocalizationService.T("main.select_build_launch"), LocalizationService.T("main.launch_title"), MessageBoxButton.OK, MessageBoxImage.Information);
                _viewModel.SetStatus(LocalizationService.T("main.no_build_selected_log"));
                RestorePlayButton();
                return;
            }

            if (validation == PlayValidationResult.UnsupportedLoader)
            {
                MessageBox.Show(
                    LocalizationService.F("main.loader_unsupported", _viewModel.CurrentBuild!.Loader),
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

            var build = _viewModel.CurrentBuild!;

            // Modpack catalog installs: content first (sets MC/loader), then vanilla/loader like classic builds.
            if (build.NeedsModpackContentInstall)
            {
                _viewModel.SetTransientPlayButton("main.downloading", enabled: false);
                var packToken = BeginDownloadUi();
                try
                {
                    var instancesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instances");
                    var installer = new ModpackInstallService(instancesRoot, _settings.CurseForgeApiKey);
                    var progress = new Progress<ModpackInstallProgress>(p =>
                    {
                        if (packToken.IsCancellationRequested)
                            return;
                        UpdateDownloadProgress(p.Percent, p.Message, lockPlayButton: true);
                    });

                    await installer.CompletePendingInstallAsync(build, progress, packToken);
                    _buildManager.UpdateBuild(build);

                    if (!string.IsNullOrWhiteSpace(build.MinecraftVersion))
                    {
                        RecentMcVersionsHelper.Record(_settings.RecentMcVersions, build.MinecraftVersion);
                        _settings.Save();
                    }

                    EndDownloadUi();
                }
                catch (Exception ex) when (HttpRetryHelper.IsCancellation(ex, packToken))
                {
                    EndDownloadUi();
                    _viewModel.SetStatus(LocalizationService.T("main.download_cancelled"));
                    _viewModel.IsDownloading = false;
                    RestorePlayButton();
                    return;
                }
                catch (Exception ex)
                {
                    EndDownloadUi();
                    _viewModel.IsDownloading = false;
                    RestorePlayButton();
                    _viewModel.SetStatus(LocalizationService.F("main.modpack_install_failed", ex.Message));
                    MessageBox.Show(
                        LocalizationService.F("main.modpack_install_failed", ex.Message),
                        LocalizationService.T("common.error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            if (!await EnsureJavaForBuildAsync(build))
            {
                RestorePlayButton();
                return;
            }

            if (!_launcherOrchestrator.IsInstalled(build))
            {
                _viewModel.SetTransientPlayButton("main.downloading", enabled: false);
                var cancellationToken = BeginDownloadUi();
                var installResult = await _launcherOrchestrator.InstallIfNeededAsync(build, cancellationToken);
                var wasCancelled = installResult == InstallFlowResult.Cancelled;
                EndDownloadUi();
                if (installResult is InstallFlowResult.Failed or InstallFlowResult.Cancelled)
                {
                    _viewModel.SetStatus(wasCancelled
                        ? LocalizationService.T("main.download_cancelled")
                        : LocalizationService.T("main.install_failed_short"));
                    if (!wasCancelled)
                        HandleInstallFailure(build, wasCancelled);
                    _viewModel.SetTransientPlayButton("main.download");
                    _viewModel.IsDownloading = false;
                    return;
                }
            }

            _viewModel.SetTransientPlayButton("main.launching", enabled: false);

            var preparation = await _launcherOrchestrator.PrepareLaunchAsync(
                build,
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
                _viewModel.SetStatus(LocalizationService.T("main.session_expired_status"));
                AddConsoleLine(LocalizationService.T("main.session_expired_console_short"));
                RestorePlayButton();
                return;
            }

            var identity = preparation.Identity;
            var ram = _viewModel.CurrentBuild!.ResolveRamGb(_settings.Ram);
            if (identity.IsOffline)
                AddConsoleLine(LocalizationService.F("main.launch_offline", ram, _viewModel.CurrentBuild.DisplayName));
            else
                AddConsoleLine(LocalizationService.F("main.launch_online", identity.Username, ram, _viewModel.CurrentBuild.DisplayName));

            _mcOutputLines.Clear();
            _gameProcess = await _launcherOrchestrator.LaunchGameAsync(_viewModel.CurrentBuild!, identity, _settings.Ram);

            if (_gameProcess != null)
            {
                _viewModel.SetStatus(LocalizationService.T("main.game_launched"));
                AddConsoleLine(LocalizationService.T("main.game_launched_console"));
                _viewModel.SetTransientPlayButton("main.game_running", enabled: false);
                var buildName = _viewModel.CurrentBuild.DisplayName;
                _ = Task.Run(() =>
                {
                    _gameProcess.WaitForExit();
                    var exitCode = _gameProcess.ExitCode;
                    Dispatcher.Invoke(() =>
                    {
                        _viewModel.IsDownloading = false;
                        _viewModel.RefreshPlayState(_minecraft.MinecraftDir);

                        if (exitCode != 0)
                        {
                            var logPath = _logService.SaveGameLog(buildName, _mcOutputLines);
                            _viewModel.SetStatus(LocalizationService.F("main.game_exit_code", exitCode));
                            AddConsoleLine(LocalizationService.F("main.game_exit_code", exitCode));
                            if (!string.IsNullOrEmpty(logPath))
                                AddConsoleLine(LocalizationService.F("main.log_saved", Path.GetFileName(logPath)));
                        }
                        else
                        {
                            _viewModel.SetStatus(LocalizationService.T("main.game_closed"));
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
            _viewModel.SetStatus(LocalizationService.F("main.error_with_message", ex.Message));
            AddConsoleLine(LocalizationService.F("main.error_with_message", ex.Message));
            RestorePlayButton();
        }
    }

    private void RestorePlayButton()
    {
        EndDownloadUi();
        _viewModel.RefreshPlayState(_minecraft.MinecraftDir);
        _viewModel.IsDownloading = false;
    }

    private void SkinBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var window = new SkinWindow(_auth, _skinService, _settings) { Owner = this };
        using (DialogShade.Begin(this))
        {
            if (window.ShowDialog() == true && window.Applied)
            {
                AddConsoleLine(LocalizationService.T("skin.applied_console"));
                _ = RefreshAvatarAsync();
            }
        }
    }

    public void SetDialogShade(bool visible)
    {
        if (DialogShadeOverlay == null)
            return;
        DialogShadeOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RefreshAvatarAsync()
    {
        try
        {
            if (_auth.IsAuthenticated())
            {
                var uuid = _auth.GetUUID();
                var token = _auth.GetAccessToken();

                // Prefer live profile skin; skip stale disk cache first attempt.
                var image = await _skinService.LoadAccountAvatarAsync(token, uuid);
                if (image == null && !string.IsNullOrWhiteSpace(uuid))
                {
                    _skinService.InvalidateUuidCache(uuid);
                    image = await _skinService.LoadSkinAsync(uuid);
                }

                if (image != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SkinImage.Source = image;
                        SkinBorder.Background = Brushes.Transparent;
                    });
                    return;
                }
            }

            await Dispatcher.InvokeAsync(ApplyLocalSkinAvatar);
        }
        catch
        {
            await Dispatcher.InvokeAsync(ApplyLocalSkinAvatar);
        }
    }

    private void ApplyLocalSkinAvatar()
    {
        SkinImage.Source = _skinService.ResolveLocalAvatar(_settings.SkinPreset, _settings.CustomSkinPath);
        SkinBorder.Background = Brushes.Transparent;
    }
}