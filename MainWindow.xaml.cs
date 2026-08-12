using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using Apeiron.Services;
using Apeiron.ViewModels;
using Forms = System.Windows.Forms;

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
    private readonly bool _pendingLaunchLastPlayed;
    private List<BuildInfo> _allBuilds = new();
    private const int MaxConsoleLines = 500;
    private Forms.NotifyIcon? _trayIcon;
    private bool _restoringFromTray;
    private bool _exitRequested;
    
    public MainWindow(SettingsService settings, string? launchTarget = null, bool launchLastPlayed = false)
    {
        InitializeComponent();
        DataContext = _viewModel;

        _settings = settings;
        _pendingLaunchTarget = launchTarget;
        _pendingLaunchLastPlayed = launchLastPlayed;
        _logService = new LogService();
        _minecraft = new MinecraftService(_settings.GetMinecraftDir());
        _minecraft.SetAssetParallelism(_settings.AssetParallelism);
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
            App.PromptWebView2IfMissing(this);
            _ = WebView2Helper.WarmupAsync(_minecraft.MinecraftDir);
            if (!_settings.OnboardingCompleted)
                ShowOnboarding();
            if (_settings.CheckForUpdates && LauncherMetadata.HasUpdateSource)
                _ = CheckForUpdatesOnStartupAsync();
            TryQuickLaunchFromArgs();
        };
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += (_, _) =>
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            DisposeTrayIcon();
        };
    }
}
