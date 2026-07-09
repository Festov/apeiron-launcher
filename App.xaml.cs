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

        var mainWindow = new MainWindow(settings);
        mainWindow.Show();
    }
}
