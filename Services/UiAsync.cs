using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Apeiron.Services;

public static class UiAsync
{
    public static async void Run(Func<Task> action, Dispatcher? dispatcher = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            var target = dispatcher ?? Application.Current?.Dispatcher;
            if (target != null && !target.CheckAccess())
            {
                target.Invoke(() => Report(ex));
                return;
            }

            Report(ex);
        }
    }

    private static void Report(Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(ex);
        try
        {
            MessageBox.Show(
                ex.Message,
                LocalizationService.T("common.error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Ignore UI failures during shutdown.
        }
    }
}
