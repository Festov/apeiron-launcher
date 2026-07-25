using System;
using System.Windows;

namespace Apeiron;

/// <summary>Dims the main window while a modal dialog is open.</summary>
internal sealed class DialogShade : IDisposable
{
    private readonly MainWindow? _main;
    private bool _disposed;

    private DialogShade(MainWindow? main)
    {
        _main = main;
        _main?.SetDialogShade(true);
    }

    public static DialogShade Begin(Window? owner = null)
    {
        var main = owner as MainWindow
                   ?? Application.Current?.MainWindow as MainWindow;
        return new DialogShade(main);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _main?.SetDialogShade(false);
    }
}
