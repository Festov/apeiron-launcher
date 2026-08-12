using System.Diagnostics;
using System.Windows;
using Apeiron.Services;

namespace Apeiron;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = new SettingsService();
        settings.Load();
        LocalizationService.Initialize(settings.Language);

        var launchArgs = LaunchArgsParser.Parse(e.Args);
        if (launchArgs.ShowHelp)
        {
            MessageBox.Show(LaunchArgsHelp.GetText(), LaunchArgsHelp.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.RequestActivate();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _singleInstance.Activated += OnExistingInstanceActivated;

        var mainWindow = new MainWindow(settings, launchArgs.LaunchTarget, launchArgs.LaunchLastPlayed);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstance != null)
        {
            _singleInstance.Activated -= OnExistingInstanceActivated;
            _singleInstance.Dispose();
            _singleInstance = null;
        }

        base.OnExit(e);
    }

    private void OnExistingInstanceActivated()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is MainWindow window)
                window.BringToForeground();
        });
    }

    public static void PromptWebView2IfMissing(Window? owner = null)
    {
        if (WebView2Helper.IsRuntimeAvailable())
            return;

        var result = MessageBox.Show(
            owner,
            LocalizationService.T("main.webview2_missing") + "\n\n" + LocalizationService.T("main.webview2_download") + "?",
            LocalizationService.T("main.webview2_missing_title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = WebView2Helper.EvergreenRuntimeDownloadUrl,
                UseShellExecute = true
            });
        }
    }
}
