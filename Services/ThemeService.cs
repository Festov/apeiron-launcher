using System.Windows;
using System.Windows.Media;

namespace Apeiron.Services;

public sealed class ThemeService
{
    private static readonly Color LightBg = Parse("#F0F2F5");
    private static readonly Color LightSurface = Parse("#FFFFFF");
    private static readonly Color LightSurfaceAlt = Parse("#F9FAFB");
    private static readonly Color LightBorder = Parse("#E5E7EB");
    private static readonly Color LightConsole = Parse("#006400");
    private static readonly Color LightStatus = Parse("#059669");

    private static readonly Color DarkBg = Parse("#1A1A2E");
    private static readonly Color DarkSurface = Parse("#252540");
    private static readonly Color DarkSurfaceAlt = Parse("#2D2D50");
    private static readonly Color DarkBorder = Parse("#3D3D6A");
    private static readonly Color DarkConsole = Colors.White;
    private static readonly Color DarkStatus = Parse("#2ECC71");
    private static readonly Color DarkAccent = Parse("#818CF8");

    public bool IsDark { get; private set; }
    public Brush WindowBackgroundBrush => CreateBrush(IsDark ? DarkBg : LightBg);
    public Brush SurfaceBrush => CreateBrush(IsDark ? DarkSurface : LightSurface);
    public Brush SurfaceAltBrush => CreateBrush(IsDark ? DarkSurfaceAlt : LightSurfaceAlt);
    public Brush BorderBrush => CreateBrush(IsDark ? DarkBorder : LightBorder);
    public Brush ConsoleBrush => CreateBrush(IsDark ? DarkConsole : LightConsole);
    public Brush StatusBrush => CreateBrush(IsDark ? DarkStatus : LightStatus);
    public Brush ProgressForegroundBrush => CreateBrush(IsDark ? DarkAccent : Parse("#6366F1"));
    public Brush ProgressBackgroundBrush => CreateBrush(IsDark ? DarkBorder : LightBorder);
    public Brush SecondaryTextBrush => CreateBrush(IsDark ? Parse("#9CA3AF") : Parse("#6B7280"));
    public Brush MutedTextBrush => CreateBrush(IsDark ? Parse("#6B7280") : Parse("#9CA3AF"));
    public Brush PrimaryTextBrush => CreateBrush(IsDark ? Colors.White : Parse("#1F2937"));
    public string ThemeIcon => IsDark ? "☀️" : "🌙";
    public string ThemeTooltipKey => IsDark ? "main.theme_light" : "main.theme_dark";

    public void Apply(bool dark)
    {
        IsDark = dark;
        var resources = Application.Current.Resources;

        resources["BgBrush"] = resources[dark ? "DarkBgBrush" : "ThemeLightBgBrush"];
        resources["SurfaceBrush"] = resources[dark ? "DarkSurfaceBrush" : "ThemeLightSurfaceBrush"];
        resources["SurfaceLightBrush"] = resources[dark ? "DarkSurfaceLightBrush" : "ThemeLightSurfaceAltBrush"];
        resources["BorderBrush"] = resources[dark ? "DarkBorderBrush" : "ThemeLightBorderBrush"];
        resources["TextPrimaryBrush"] = resources[dark ? "DarkTextPrimaryBrush" : "ThemeLightTextPrimaryBrush"];
        resources["TextSecondaryBrush"] = resources[dark ? "DarkTextSecondaryBrush" : "ThemeLightTextSecondaryBrush"];
        resources["TextMutedBrush"] = resources[dark ? "DarkTextMutedBrush" : "ThemeLightTextMutedBrush"];
        resources["ConsoleTextBrush"] = resources[dark ? "DarkConsoleTextBrush" : "ThemeLightConsoleTextBrush"];
        resources["HoverBackgroundBrush"] = resources[dark ? "DarkHoverBackgroundBrush" : "ThemeLightHoverBackgroundBrush"];
        resources["HoverBorderBrush"] = resources[dark ? "DarkHoverBorderBrush" : "ThemeLightHoverBorderBrush"];
        resources["HoverForegroundBrush"] = resources[dark ? "DarkHoverForegroundBrush" : "ThemeLightHoverForegroundBrush"];
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Parse(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex)!;
}
