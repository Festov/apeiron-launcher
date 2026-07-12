using System;

namespace Apeiron.Services;

public static class LaunchArgsHelp
{
    public const string Title = "Apeiron Launcher";

    public static string GetText() =>
        """
        Apeiron Launcher — command-line options

          --launch <name>     Launch instance by name, display name, or ID
          --launch=<name>     Same as above
          --help, -h, /?      Show this help

        Examples:
          Apeiron.exe --launch "1.20.1 - Fabric"
          Apeiron.exe --launch=abc123-build-id
        """;
}
