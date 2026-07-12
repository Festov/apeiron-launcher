using System;

namespace Apeiron.Services;

public readonly record struct ParsedLaunchArgs(string? LaunchTarget, bool ShowHelp)
{
    public bool HasLaunchTarget => !string.IsNullOrWhiteSpace(LaunchTarget);
}

public static class LaunchArgsParser
{
    public static ParsedLaunchArgs Parse(string[] args)
    {
        string? launchTarget = null;
        var showHelp = false;

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
                if (i + 1 < args.Length)
                    launchTarget = args[++i].Trim();
                continue;
            }

            if (arg.StartsWith("--launch=", StringComparison.OrdinalIgnoreCase))
                launchTarget = arg["--launch=".Length..].Trim();
        }

        return new ParsedLaunchArgs(
            string.IsNullOrWhiteSpace(launchTarget) ? null : launchTarget,
            showHelp);
    }

    private static bool IsHelpFlag(string arg) =>
        arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-?", StringComparison.OrdinalIgnoreCase);
}
