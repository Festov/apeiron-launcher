using System.Windows;
using Apeiron.Services;

namespace Apeiron;

public partial class App : Application
{
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

        var mainWindow = new MainWindow(settings, launchArgs.LaunchTarget);
        mainWindow.Show();
    }
}
