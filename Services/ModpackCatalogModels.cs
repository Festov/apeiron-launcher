using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Apeiron.Services;

public enum ModpackSource
{
    Modrinth,
    CurseForge
}

public sealed class ModpackListItem : INotifyPropertyChanged
{
    private ImageSource? _iconImage;

    public ModpackSource Source { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
    public long Downloads { get; init; }
    public string Author { get; init; } = "";
    public string? IconUrl { get; init; }
    public string? LatestVersionId { get; init; }
    public string? GameVersionsHint { get; init; }
    public string? LoadersHint { get; init; }

    public ImageSource? IconImage
    {
        get => _iconImage;
        set
        {
            if (ReferenceEquals(_iconImage, value))
                return;
            _iconImage = value;
            OnPropertyChanged();
        }
    }

    public string DisplayLine =>
        string.IsNullOrWhiteSpace(GameVersionsHint) && string.IsNullOrWhiteSpace(LoadersHint)
            ? $"{Name}  ·  {FormatDownloads(Downloads)}"
            : $"{Name}  ·  {FormatDownloads(Downloads)}  ·  {JoinHints(GameVersionsHint, LoadersHint)}";

    public string PlatformLabel => Source switch
    {
        ModpackSource.Modrinth => "Modrinth",
        ModpackSource.CurseForge => "CurseForge",
        _ => ""
    };

    public bool ShowPlatformInSubtitle { get; set; }

    public string SubtitleLine
    {
        get
        {
            var downloads = FormatDownloads(Downloads);
            var hints = JoinHints(GameVersionsHint, LoadersHint);
            var core = string.IsNullOrWhiteSpace(hints) ? downloads : $"{downloads}  ·  {hints}";
            return ShowPlatformInSubtitle && !string.IsNullOrEmpty(PlatformLabel)
                ? $"{PlatformLabel}  ·  {core}"
                : core;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => DisplayLine;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string JoinHints(string? game, string? loaders)
    {
        if (!string.IsNullOrWhiteSpace(game) && !string.IsNullOrWhiteSpace(loaders))
            return $"{game} / {loaders}";
        return game ?? loaders ?? "";
    }

    private static string FormatDownloads(long count)
    {
        if (count >= 1_000_000)
            return $"{count / 1_000_000.0:0.#}M";
        if (count >= 1_000)
            return $"{count / 1_000.0:0.#}K";
        return count.ToString();
    }
}

public sealed class ModpackInstallProgress
{
    public string Message { get; init; } = "";
    /// <summary>Determinate 0–100 for the main-window progress bar.</summary>
    public int Percent { get; init; }
    public int Completed { get; init; }
    public int Total { get; init; }
}

public sealed class ParsedModpackMetadata
{
    public string Name { get; init; } = "";
    public string MinecraftVersion { get; init; } = "";
    public string Loader { get; init; } = "";
    public string LoaderVersion { get; init; } = "";
}
