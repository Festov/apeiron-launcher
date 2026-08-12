using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Apeiron.Services;

namespace Apeiron;

public partial class SettingsWindow : Window, ILocalizable
{
    private sealed class LanguageOption
    {
        public string Code { get; init; } = "";
        public string Display { get; set; } = "";
    }

    private sealed class JvmProfileOption
    {
        public string Id { get; init; } = "";
        public string Display { get; set; } = "";
    }

    private readonly SettingsService _settings;
    private readonly LogService _logService;

    public bool Saved { get; private set; }

    public SettingsWindow(SettingsService settings, LogService logService)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        RoundedDialogChrome.Attach(DialogChrome);
        _settings = settings;
        _logService = logService;

        RamTextBox.Text = settings.Ram.ToString();
        CheckForUpdatesCheckBox.IsChecked = settings.CheckForUpdates;
        MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTray;
        CloseOnGameLaunchCheckBox.IsChecked = settings.CloseOnGameLaunch;
        McFolderTextBox.Text = settings.MinecraftDir;
        McFolderTextBox.ToolTip = settings.GetDefaultMinecraftDir();

        Loaded += (_, _) =>
        {
            ApplyLocalization();
            SelectLanguage(settings.Language);
            SelectJvmProfile(settings.JvmProfile);
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        };
        Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(ApplyLocalization);

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("settings.title");
        TitleText.Text = LocalizationService.T("settings.title");
        RamLabel.Text = LocalizationService.T("settings.ram");
        LanguageLabel.Text = LocalizationService.T("settings.language");
        McFolderLabel.Text = LocalizationService.T("settings.mc_folder");
        McFolderHintText.Text = LocalizationService.T("settings.mc_hint") + "\n" + LocalizationService.T("settings.mc_restart_hint");
        McFolderBrowseButton.ToolTip = LocalizationService.T("settings.mc_browse");
        CheckForUpdatesCheckBox.Content = LocalizationService.T("settings.check_updates");
        MinimizeToTrayCheckBox.Content = LocalizationService.T("settings.tray");
        CloseOnGameLaunchCheckBox.Content = LocalizationService.T("settings.close_on_launch");
        JvmProfileLabel.Text = LocalizationService.T("settings.jvm_profile");
        OpenLogsButton.Content = LocalizationService.T("settings.open_logs");
        CheckUpdatesButton.Content = LocalizationService.T("settings.check_updates_now");
        CancelBtn.Content = LocalizationService.T("common.cancel");
        SaveBtn.Content = LocalizationService.T("common.save");

        var selected = LanguageComboBox.SelectedValue as string ?? _settings.Language;
        LanguageComboBox.ItemsSource = new[]
        {
            new LanguageOption { Code = "auto", Display = LocalizationService.T("lang.auto") },
            new LanguageOption { Code = "en", Display = LocalizationService.T("lang.en") },
            new LanguageOption { Code = "ru", Display = LocalizationService.T("lang.ru") },
            new LanguageOption { Code = "zh", Display = LocalizationService.T("lang.zh") }
        };
        SelectLanguage(selected);

        var jvmSelected = JvmProfileComboBox.SelectedValue as string ?? _settings.JvmProfile;
        JvmProfileComboBox.ItemsSource = JvmProfileHelper.Profiles
            .Select(p => new JvmProfileOption { Id = p.Id, Display = LocalizationService.T(p.LocKey) })
            .ToArray();
        SelectJvmProfile(jvmSelected);

        var version = LauncherAppVersion.FullDisplay;
        VersionText.Text = LocalizationService.F("settings.version", version);
    }

    private void SelectLanguage(string code)
    {
        foreach (LanguageOption item in LanguageComboBox.Items)
        {
            if (item.Code == code)
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }
        LanguageComboBox.SelectedIndex = 0;
    }

    private void SelectJvmProfile(string id)
    {
        foreach (JvmProfileOption item in JvmProfileComboBox.Items)
        {
            if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                JvmProfileComboBox.SelectedItem = item;
                return;
            }
        }
        if (JvmProfileComboBox.Items.Count > 0)
            JvmProfileComboBox.SelectedIndex = 0;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) =>
        LogService.OpenLogsFolder(_logService.LogsDirectory);

    private void McFolderBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizationService.T("settings.mc_browse"),
            SelectedPath = string.IsNullOrWhiteSpace(McFolderTextBox.Text)
                ? _settings.GetDefaultMinecraftDir()
                : McFolderTextBox.Text.Trim(),
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            McFolderTextBox.Text = dialog.SelectedPath;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) =>
        await CheckUpdatesAsync(manual: true);

    private async Task CheckUpdatesAsync(bool manual)
    {
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            if (!LauncherMetadata.HasUpdateSource)
            {
                if (manual)
                {
                    MessageBox.Show(
                        LocalizationService.T("settings.update_not_configured"),
                        LocalizationService.T("settings.check_updates_now"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            var update = await LauncherUpdateService.CheckForUpdateAsync(LauncherMetadata.GitHubRepository);
            if (update == null)
            {
                if (manual)
                {
                    MessageBox.Show(
                        LocalizationService.T("settings.update_none"),
                        LocalizationService.T("settings.check_updates_now"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

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
        catch (Exception ex)
        {
            if (manual)
            {
                MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RamTextBox.Text.Trim(), out var ram) || ram < 1 || ram > SystemMemoryHelper.GetRecommendedMaxRamGb())
        {
            MessageBox.Show(
                LocalizationService.F("settings.ram_invalid", SystemMemoryHelper.GetRecommendedMaxRamGb()),
                LocalizationService.T("common.settings"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var language = (LanguageComboBox.SelectedItem as LanguageOption)?.Code ?? "auto";
        var mcDir = McFolderTextBox.Text.Trim();
        var previousMcDir = _settings.GetMinecraftDir();

        if (!string.IsNullOrWhiteSpace(mcDir) && !Directory.Exists(mcDir))
        {
            var create = MessageBox.Show(
                LocalizationService.F("settings.folder_missing", mcDir),
                LocalizationService.T("common.settings"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (create != MessageBoxResult.Yes)
                return;
            Directory.CreateDirectory(mcDir);
        }

        _settings.Ram = ram;
        _settings.Language = language;
        _settings.CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
        _settings.CloseOnGameLaunch = CloseOnGameLaunchCheckBox.IsChecked == true;
        _settings.JvmProfile = (JvmProfileComboBox.SelectedItem as JvmProfileOption)?.Id ?? JvmProfileHelper.Default;
        _settings.MinecraftDir = mcDir;
        _settings.Save();

        LocalizationService.Instance.ApplySetting(language);

        if (!string.Equals(previousMcDir, _settings.GetMinecraftDir(), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                LocalizationService.T("main.minecraft_dir_changed"),
                LocalizationService.T("common.settings"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        Saved = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
