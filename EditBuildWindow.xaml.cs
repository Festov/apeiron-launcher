using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Apeiron.Services;
using Microsoft.Win32;

namespace Apeiron;

public partial class EditBuildWindow : Window, ILocalizable
{
    private readonly BuildInfo _build;
    private readonly SettingsService _settings;
    private List<ModManager.ModEntry> _mods = new();

    public BuildInfo? SavedBuild { get; private set; }
    public bool ReinstallRequested { get; private set; }
    public bool DeleteRequested { get; private set; }
    public bool DuplicateRequested { get; private set; }

    public EditBuildWindow(BuildInfo build, SettingsService settings)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _build = build;
        _settings = settings;

        NameTextBox.Text = build.Name;
        DefaultBuildCheckBox.IsChecked = build.Id == settings.DefaultBuildId;
        JvmArgsTextBox.Text = build.JvmArgs;
        UseGlobalRamCheckBox.IsChecked = build.RamGb <= 0;
        RamGbTextBox.Text = build.RamGb > 0 ? build.RamGb.ToString() : _settings.Ram.ToString();
        RamGbTextBox.IsEnabled = build.RamGb > 0;
        ResolutionWidthTextBox.Text = build.ResolutionWidth > 0 ? build.ResolutionWidth.ToString() : "";
        ResolutionHeightTextBox.Text = build.ResolutionHeight > 0 ? build.ResolutionHeight.ToString() : "";
        FullscreenCheckBox.IsChecked = build.Fullscreen;
        ProfileInfoText.Text = build.IsModded
            ? $"{build.MinecraftVersion} · {build.Loader} {build.LoaderVersion}".Trim()
            : $"Vanilla {build.MinecraftVersion}";

        if (build.IsModded)
        {
            ModsEnabledRow.Visibility = Visibility.Visible;
            ModsSection.Visibility = Visibility.Visible;
            ModsEnabledCheckBox.IsChecked = build.ModsEnabled;
            LoadMods();
        }

        Loaded += (_, _) =>
        {
            ApplyLocalization();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        };
        Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => Dispatcher.Invoke(ApplyLocalization);

    public void ApplyLocalization()
    {
        Title = LocalizationService.T("edit_build.window_title");
        TitleText.Text = LocalizationService.T("edit_build.title");
        NameLabel.Text = LocalizationService.T("edit_build.name");
        ProfileLabel.Text = LocalizationService.T("edit_build.profile");
        ModsLabel.Text = LocalizationService.T("edit_build.mods");
        ModsEnabledCheckBox.Content = LocalizationService.T("edit_build.mods_enabled");
        ModsListLabel.Text = LocalizationService.T("edit_build.mods_list");
        AddModButton.Content = LocalizationService.T("edit_build.add_mod");
        OpenModsFolderButton.Content = LocalizationService.T("edit_build.open_mods_folder");
        RefreshModsButton.Content = LocalizationService.T("edit_build.refresh_mods");
        NoModsText.Text = LocalizationService.T("edit_build.no_mods");
        LaunchLabel.Text = LocalizationService.T("edit_build.launch");
        DefaultBuildCheckBox.Content = LocalizationService.T("edit_build.default_build");
        JvmLabel.Text = LocalizationService.T("edit_build.jvm");
        JvmArgsTextBox.ToolTip = LocalizationService.T("edit_build.jvm_tooltip");
        RamLabel.Text = LocalizationService.T("edit_build.ram");
        UseGlobalRamCheckBox.Content = LocalizationService.T("edit_build.ram_use_global");
        RamGbTextBox.ToolTip = LocalizationService.T("edit_build.ram_tooltip");
        WindowLabel.Text = LocalizationService.T("edit_build.window");
        ResolutionWidthTextBox.ToolTip = LocalizationService.T("edit_build.width_tooltip");
        ResolutionHeightTextBox.ToolTip = LocalizationService.T("edit_build.height_tooltip");
        FullscreenCheckBox.Content = LocalizationService.T("edit_build.fullscreen");
        FolderButton.Content = LocalizationService.T("edit_build.folder");
        FolderButton.ToolTip = LocalizationService.T("edit_build.folder_tooltip");
        ExportButton.Content = LocalizationService.T("edit_build.export");
        ImportButton.Content = LocalizationService.T("edit_build.import");
        BackupButton.Content = LocalizationService.T("edit_build.backup");
        BackupButton.ToolTip = LocalizationService.T("edit_build.backup_tooltip");
        DuplicateButton.Content = LocalizationService.T("edit_build.duplicate");
        ReinstallButton.Content = LocalizationService.T("edit_build.reinstall");
        DeleteButton.Content = LocalizationService.T("edit_build.delete");
        CancelBtn.Content = LocalizationService.T("common.cancel");
        SaveBtn.Content = LocalizationService.T("common.save");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void LoadMods()
    {
        _mods = ModManager.ListMods(_build.GetModsDir());
        ModsList.ItemsSource = null;
        ModsList.ItemsSource = _mods;
        NoModsText.Visibility = _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ModCheckBox_Changed(object sender, RoutedEventArgs e) { }

    private void OpenInstanceFolder_Click(object sender, RoutedEventArgs e)
    {
        var gameDir = _build.GetGameDir();
        _build.EnsureInstanceFolders();
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = gameDir,
            UseShellExecute = true
        });
    }

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        var modsPath = _build.GetModsDir();
        Directory.CreateDirectory(modsPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = modsPath,
            UseShellExecute = true
        });
    }

    private void RefreshMods_Click(object sender, RoutedEventArgs e) => LoadMods();

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("edit_build.export_title"),
            Filter = LocalizationService.T("edit_build.export_filter"),
            FileName = _build.DisplayName + ".zip"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            BuildExportService.Export(_build, dialog.FileName, BuildExportMode.Modpack);
            MessageBox.Show(
                LocalizationService.T("edit_build.export_done"),
                LocalizationService.T("edit_build.export"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("edit_build.backup_title"),
            Filter = LocalizationService.T("edit_build.export_filter"),
            FileName = _build.DisplayName + "-backup.zip"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            BuildExportService.Export(_build, dialog.FileName, BuildExportMode.FullBackup);
            MessageBox.Show(
                LocalizationService.T("edit_build.backup_done"),
                LocalizationService.T("edit_build.backup"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UseGlobalRamCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var useGlobal = UseGlobalRamCheckBox.IsChecked == true;
        RamGbTextBox.IsEnabled = !useGlobal;
        if (useGlobal)
            RamGbTextBox.Text = _settings.Ram.ToString();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("edit_build.import_title"),
            Filter = LocalizationService.T("edit_build.export_filter")
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            BuildExportService.ImportInto(dialog.FileName, _build);
            LoadMods();
            MessageBox.Show(
                LocalizationService.T("edit_build.import_done"),
                LocalizationService.T("edit_build.import"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddMod_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("edit_build.select_mods"),
            Filter = LocalizationService.T("edit_build.mod_filter"),
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
            return;

        ImportModFiles(dialog.FileNames);
    }

    private void ModsSection_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ModsSection_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        ImportModFiles(files);
    }

    private void ImportModFiles(IEnumerable<string> files)
    {
        var modsDir = _build.GetModsDir();
        var count = ModManager.ImportMods(modsDir, files);
        LoadMods();

        if (count > 0)
            MessageBox.Show(LocalizationService.F("edit_build.mods_added", count), LocalizationService.T("common.mods"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReinstallButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            LocalizationService.F("edit_build.reinstall_confirm", _build.DisplayName),
            LocalizationService.T("edit_build.reinstall_title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        ReinstallRequested = true;
        DialogResult = true;
        Close();
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            LocalizationService.F("edit_build.duplicate_confirm", _build.DisplayName),
            LocalizationService.T("edit_build.duplicate_title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        DuplicateRequested = true;
        DialogResult = true;
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            LocalizationService.F("edit_build.delete_confirm", _build.Name),
            LocalizationService.T("edit_build.delete_title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        DeleteRequested = true;
        DialogResult = true;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = BuildInfo.GenerateDefaultName(
                _build.MinecraftVersion, _build.Loader, _build.LoaderVersion, _build.IsModded);
        }

        _build.Name = name;

        _build.JvmArgs = JvmArgsTextBox.Text.Trim();
        _build.RamGb = UseGlobalRamCheckBox.IsChecked == true
            ? 0
            : ParsePositiveInt(RamGbTextBox.Text);
        _build.ResolutionWidth = ParsePositiveInt(ResolutionWidthTextBox.Text);
        _build.ResolutionHeight = ParsePositiveInt(ResolutionHeightTextBox.Text);
        _build.Fullscreen = FullscreenCheckBox.IsChecked == true;

        if (_build.IsModded)
        {
            _build.ModsEnabled = ModsEnabledCheckBox.IsChecked == true;

            if (!_build.ModsEnabled)
                ModManager.SetAllModsEnabled(_build.GetModsDir(), false);
            else
                ModManager.ApplyModStates(_build.GetModsDir(), _mods);
        }

        SavedBuild = _build;

        if (DefaultBuildCheckBox.IsChecked == true)
            _settings.DefaultBuildId = _build.Id;
        else if (_settings.DefaultBuildId == _build.Id)
            _settings.DefaultBuildId = "";
        _settings.Save();

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
    }

    private static int ParsePositiveInt(string text) =>
        int.TryParse(text.Trim(), out var value) && value > 0 ? value : 0;
}
