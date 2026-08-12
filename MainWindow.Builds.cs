using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Collections.Generic;
using Apeiron.Services;

namespace Apeiron;

public partial class MainWindow
{
    private void LoadBuilds()
    {
        try
        {
            _allBuilds = _buildManager.LoadBuilds();
            PopulateBuildsCombo(_allBuilds);
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

        SelectBuildInCombo(
            !string.IsNullOrWhiteSpace(selectBuildId) ? selectBuildId
            : !string.IsNullOrWhiteSpace(_settings.DefaultBuildId) ? _settings.DefaultBuildId
            : _settings.LastPlayedBuildId);
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
        var build = BuildListHelper.FindByIdOrName(_allBuilds, key)
                    ?? BuildListHelper.FindByIdOrName(BuildsComboBox.Items.OfType<BuildInfo>(), key);
        if (build == null)
            return false;

        PopulateBuildsCombo(_allBuilds, build.Id);
        BuildsComboBox.SelectedItem = BuildsComboBox.Items.OfType<BuildInfo>()
            .FirstOrDefault(b => b.Id == build.Id) ?? build;
        _viewModel.CurrentBuild = build;
        CheckSelectedBuildInstalled();
        return true;
    }

    private void TryQuickLaunchFromArgs()
    {
        if (_pendingLaunchLastPlayed)
        {
            if (string.IsNullOrWhiteSpace(_settings.LastPlayedBuildId) ||
                !TrySelectBuildByIdOrName(_settings.LastPlayedBuildId))
            {
                AddConsoleLine(LocalizationService.T("main.launch_last_missing"));
                return;
            }

            AddConsoleLine(LocalizationService.F("main.cli_launch", _viewModel.CurrentBuild!.DisplayName));
            UiAsync.Run(PlayButton_ClickAsync, Dispatcher);
            return;
        }

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

            if (editWindow.VerifyRequested)
                await VerifyBuildAsync(_viewModel.CurrentBuild!);

            if (editWindow.OpenModBrowserRequested)
                await OpenModBrowserAsync(_viewModel.CurrentBuild!);

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

    private async Task VerifyBuildAsync(BuildInfo build)
    {
        _viewModel.IsDownloading = true;
        _viewModel.SetTransientPlayButton("verify.checking", enabled: false);
        _viewModel.SetStatus(LocalizationService.T("verify.checking"));
        AddConsoleLine(LocalizationService.F("verify.start", build.DisplayName));

        try
        {
            var verifier = new MinecraftVerifyService(_minecraft);
            verifier.Log += msg => Dispatcher.Invoke(() => AddConsoleLine(msg));
            verifier.ProgressChanged += (p, t) =>
                Dispatcher.Invoke(() => UpdateDownloadProgress(p, t, lockPlayButton: true));

            var result = await verifier.VerifyBuildAsync(build);
            _viewModel.SetStatus(result.Message);
            AddConsoleLine(result.Message);
            MessageBox.Show(
                result.Message,
                LocalizationService.T("verify.title"),
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus(ex.Message);
            MessageBox.Show(ex.Message, LocalizationService.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _viewModel.IsDownloading = false;
            _viewModel.HideProgressPanel();
            RestorePlayButton();
        }
    }

    private async Task OpenModBrowserAsync(BuildInfo build)
    {
        var browser = new ModBrowserWindow(build, _settings) { Owner = this };
        using (DialogShade.Begin(this))
        {
            if (browser.ShowDialog() == true && browser.Installed)
            {
                AddConsoleLine(LocalizationService.F("mod_browser.installed", build.DisplayName));
                _viewModel.SetStatus(LocalizationService.T("mod_browser.install_done"));
            }
        }
        await Task.CompletedTask;
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
}
