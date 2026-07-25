using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Apeiron;

internal static class RoundedDialogChrome
{
    public const double CornerRadius = 14;

    public static void Attach(Border chrome) =>
        AttachClip(chrome, CornerRadius);

    public static void AttachClip(FrameworkElement element, double radius)
    {
        void Apply(object? _, SizeChangedEventArgs? __) => ApplyClip(element, radius);

        element.SizeChanged += Apply;
        element.Loaded += (_, _) => ApplyClip(element, radius);
        if (element.IsLoaded)
            ApplyClip(element, radius);
    }

    public static void ApplyClip(FrameworkElement element, double radius)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return;

        // Fresh geometry each time — WPF does not reliably refresh in-place RadiusX/Y updates.
        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius);
    }
}
