using System;

namespace Apeiron.Services;

public readonly record struct ParsedLaunchArgs(string? LaunchTarget, bool ShowHelp, bool LaunchLastPlayed)
{
    public bool HasLaunchTarget => !string.IsNullOrWhiteSpace(LaunchTarget);
    public bool ShouldQuickLaunch => HasLaunchTarget || LaunchLastPlayed;
}

public static class LaunchArgsParser
{
    public static ParsedLaunchArgs Parse(string[] args)
    {
        string? launchTarget = null;
        var showHelp = false;
        var launchLastPlayed = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (IsHelpFlag(arg))
            {
                showHelp = true;
                continue;
            }

            if (arg.Equals("--launch", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-launch", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    launchTarget = args[++i].Trim();
                else
                    launchLastPlayed = true;
                continue;
            }

            if (arg.StartsWith("--launch=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg["--launch=".Length..].Trim();
                if (string.IsNullOrWhiteSpace(value))
                    launchLastPlayed = true;
                else
                    launchTarget = value;
            }
        }

        return new ParsedLaunchArgs(
            string.IsNullOrWhiteSpace(launchTarget) ? null : launchTarget,
            showHelp,
            launchLastPlayed);
    }

    private static bool IsHelpFlag(string arg) =>
        arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-?", StringComparison.OrdinalIgnoreCase);
}
