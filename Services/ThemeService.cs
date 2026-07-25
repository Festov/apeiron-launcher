using System.Windows;
using System.Windows.Media;

namespace Apeiron.Services;

/// <summary>
/// Single-theme palette (Obsidian Ember). Dual light/dark switching was removed.
/// </summary>
public sealed class ThemeService
{
    private static readonly Color Bg = Parse("#0A0C10");
    private static readonly Color Surface = Parse("#12151C");
    private static readonly Color SurfaceAlt = Parse("#1A1E28");
    private static readonly Color SurfaceRaised = Parse("#222733");
    private static readonly Color Border = Parse("#2C3342");
    private static readonly Color ConsoleBg = Parse("#07090D");
    private static readonly Color Console = Parse("#C8E6C0");
    private static readonly Color Status = Parse("#4ADE80");
    private static readonly Color Accent = Parse("#E8A54B");
    private static readonly Color TextPrimary = Parse("#F3F0E8");
    private static readonly Color TextSecondary = Parse("#A8B0C0");
    private static readonly Color TextMuted = Parse("#6E7689");

    public bool IsDark => true;
    public Brush WindowBackgroundBrush => CreateBrush(Bg);
    public Brush SurfaceBrush => CreateBrush(Surface);
    public Brush SurfaceAltBrush => CreateBrush(SurfaceAlt);
    public Brush SurfaceRaisedBrush => CreateBrush(SurfaceRaised);
    public Brush BorderBrush => CreateBrush(Border);
    public Brush ConsoleBackgroundBrush => CreateBrush(ConsoleBg);
    public Brush ConsoleBrush => CreateBrush(Console);
    public Brush StatusBrush => CreateBrush(Status);
    public Brush ProgressForegroundBrush => CreateBrush(Accent);
    public Brush ProgressBackgroundBrush => CreateBrush(SurfaceRaised);
    public Brush SecondaryTextBrush => CreateBrush(TextSecondary);
    public Brush MutedTextBrush => CreateBrush(TextMuted);
    public Brush PrimaryTextBrush => CreateBrush(TextPrimary);

    public void Apply()
    {
        var resources = Application.Current?.Resources;
        if (resources == null)
            return;

        // Keep DynamicResource keys aligned with the single theme (idempotent).
        resources["BgBrush"] = CreateBrush(Bg);
        resources["SurfaceBrush"] = CreateBrush(Surface);
        resources["SurfaceLightBrush"] = CreateBrush(SurfaceAlt);
        resources["SurfaceRaisedBrush"] = CreateBrush(SurfaceRaised);
        resources["BorderBrush"] = CreateBrush(Border);
        resources["TextPrimaryBrush"] = CreateBrush(TextPrimary);
        resources["TextSecondaryBrush"] = CreateBrush(TextSecondary);
        resources["TextMutedBrush"] = CreateBrush(TextMuted);
        resources["ConsoleTextBrush"] = CreateBrush(Console);
        resources["ConsoleBgBrush"] = CreateBrush(ConsoleBg);
        resources["HoverBackgroundBrush"] = CreateBrush(SurfaceRaised);
        resources["HoverBorderBrush"] = CreateBrush(Accent);
        resources["HoverForegroundBrush"] = CreateBrush(Accent);
        resources["AccentBrush"] = CreateBrush(Accent);
        resources["SuccessBrush"] = CreateBrush(Status);
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
