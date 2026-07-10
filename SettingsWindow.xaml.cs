using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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

    private readonly SettingsService _settings;
    private readonly LogService _logService;

    public bool Saved { get; private set; }

    public SettingsWindow(SettingsService settings, LogService logService)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _settings = settings;
        _logService = logService;

        RamTextBox.Text = settings.Ram.ToString();
        OfflineOnlyCheckBox.IsChecked = settings.OfflineOnly;
        CheckForUpdatesCheckBox.IsChecked = settings.CheckForUpdates;

        Loaded += (_, _) =>
        {
            ApplyLocalization();
            SelectLanguage(settings.Language);
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
        OfflineOnlyCheckBox.Content = LocalizationService.T("settings.offline_only");
        CheckForUpdatesCheckBox.Content = LocalizationService.T("settings.check_updates");
        OpenLogsButton.Content = LocalizationService.T("settings.open_logs");
        CheckUpdatesButton.Content = LocalizationService.T("settings.check_updates_now");
        CancelBtn.Content = LocalizationService.T("common.cancel");
        SaveBtn.Content = LocalizationService.T("common.save");

        var selected = LanguageComboBox.SelectedValue as string ?? _settings.Language;
        LanguageComboBox.ItemsSource = new[]
        {
            new LanguageOption { Code = "auto", Display = LocalizationService.T("lang.auto") },
            new LanguageOption { Code = "en", Display = LocalizationService.T("lang.en") },
            new LanguageOption { Code = "ru", Display = LocalizationService.T("lang.ru") }
        };
        SelectLanguage(selected);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version != null
            ? LocalizationService.F("settings.version", $"{version.Major}.{version.Minor}.{version.Build}")
            : LocalizationService.F("settings.version", "1.2.0");
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) =>
        LogService.OpenLogsFolder(_logService.LogsDirectory);

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

        _settings.Ram = ram;
        _settings.Language = language;
        _settings.OfflineOnly = OfflineOnlyCheckBox.IsChecked == true;
        _settings.CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true;
        _settings.Save();

        LocalizationService.Instance.ApplySetting(language);

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
