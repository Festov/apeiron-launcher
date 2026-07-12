using System;

namespace Apeiron.Services;

public static class LauncherUpdatePromptHelper
{
    private const int MaxReleaseNotesLength = 600;

    public static string BuildPrompt(LauncherUpdateInfo update, Version currentVersion)
    {
        var prompt = LocalizationService.F(
            "settings.update_available",
            update.LatestVersion,
            currentVersion);

        if (string.IsNullOrWhiteSpace(update.ReleaseNotes))
            return prompt;

        var notes = update.ReleaseNotes.Trim();
        if (notes.Length > MaxReleaseNotesLength)
            notes = notes[..MaxReleaseNotesLength].TrimEnd() + "...";

        return $"{prompt}\n\n{notes}";
    }
}
