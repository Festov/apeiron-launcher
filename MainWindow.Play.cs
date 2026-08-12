using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.IO;
using Apeiron.Services;

namespace Apeiron;

public partial class MainWindow
{
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

    private void UpdateDownloadProgress(int progress, string text, bool lockPlayButton = false)
    {
        _viewModel.ApplyDownloadProgress(progress, text);

        if (lockPlayButton)
            _viewModel.SetTransientPlayButton("main.downloading", enabled: false);
    }

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
            {
                AddConsoleLine(LocalizationService.F("main.launch_offline", ram, _viewModel.CurrentBuild.DisplayName));
                OfflineSkinHelper.ApplyForOfflineLaunch(
                    _viewModel.CurrentBuild!,
                    _settings,
                    _skinService,
                    AddConsoleLine);
            }
            else
                AddConsoleLine(LocalizationService.F("main.launch_online", identity.Username, ram, _viewModel.CurrentBuild.DisplayName));

            _mcOutputLines.Clear();
            _gameProcess = await _launcherOrchestrator.LaunchGameAsync(
                _viewModel.CurrentBuild!,
                identity,
                _settings.Ram,
                _settings.JvmProfile);

            if (_gameProcess != null)
            {
                _settings.LastPlayedBuildId = _viewModel.CurrentBuild!.Id;
                _settings.Save();
                SelectBuildInCombo(_viewModel.CurrentBuild.Id);

                _viewModel.SetStatus(LocalizationService.T("main.game_launched"));
                AddConsoleLine(LocalizationService.T("main.game_launched_console"));
                _viewModel.SetTransientPlayButton("main.game_running", enabled: false);

                if (_settings.CloseOnGameLaunch)
                {
                    if (_settings.MinimizeToTray)
                        HideToTray();
                    else
                    {
                        _exitRequested = true;
                        Close();
                        return;
                    }
                }

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
}
