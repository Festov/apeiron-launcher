using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using System.IO;
using Apeiron.Services;
using Forms = System.Windows.Forms;

namespace Apeiron;

public partial class MainWindow
{
    private void ShowOnboarding()
    {
        var onboard = new OnboardingWindow(_settings) { Owner = this };
        using (DialogShade.Begin(this))
        {
            if (onboard.ShowDialog() != true)
                return;
        }

        ApplyOfflineOnlyMode();
        if (onboard.CreateInstanceRequested)
            AddBuild_Click(this, new RoutedEventArgs());
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_restoringFromTray)
            return;

        if (_settings.MinimizeToTray && WindowState == WindowState.Minimized)
            HideToTray();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested || !_settings.MinimizeToTray)
            return;

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        EnsureTrayIcon();
        if (_trayIcon != null)
            _trayIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null)
            return;

        var icon = LoadAppIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Apeiron",
            Icon = icon ?? System.Drawing.SystemIcons.Application,
            Visible = false
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new Forms.ContextMenuStrip();
        var openItem = new Forms.ToolStripMenuItem(LocalizationService.T("tray.open"));
        openItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new Forms.ToolStripMenuItem(LocalizationService.T("tray.exit"));
        exitItem.Click += (_, _) =>
        {
            _exitRequested = true;
            DisposeTrayIcon();
            Close();
        };
        menu.Items.Add(openItem);
        menu.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = menu;
    }

    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return System.Drawing.Icon.ExtractAssociatedIcon(path);
        }
        catch { }

        try
        {
            var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
            if (!File.Exists(icoPath))
                icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(icoPath))
                return new System.Drawing.Icon(icoPath);
        }
        catch { }

        return null;
    }

    private void RestoreFromTray() => BringToForeground();

    /// <summary>Show and focus the main window (tray, minimized, or behind other windows).</summary>
    public void BringToForeground()
    {
        _restoringFromTray = true;
        try
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            if (_trayIcon != null)
                _trayIcon.Visible = false;
        }
        finally
        {
            _restoringFromTray = false;
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon == null)
            return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
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
            var newExe = LauncherUpdateService.ResolveUpdatedExecutable(update, zipPath);
            LauncherUpdateService.ScheduleApplyUpdate(newExe);
            Application.Current.Shutdown();
        }
        catch
        {
            // Silent on startup failures
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
        _minecraft.SetAssetParallelism(_settings.AssetParallelism);
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
        if (_settings.MinimizeToTray)
        {
            HideToTray();
            return;
        }

        _exitRequested = true;
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
