using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Apeiron.Services;

/// <summary>
/// Shared launch/install rules learned from CurseForge/Modrinth pack failures
/// (StoneBlock NeoForge module clash, RLCraft legacy Forge args, CDN 403, etc.).
/// </summary>
public static class LaunchProfileHelper
{
    private static readonly Regex PlaceholderRegex = new(@"\$\{[^}]+\}", RegexOptions.Compiled);

    /// <summary>Modern NeoForge/Forge use BootstrapLauncher; game comes from libraries, not versions/*.jar.</summary>
    public static bool UsesBootstrapLauncher(string? mainClass) =>
        !string.IsNullOrWhiteSpace(mainClass) &&
        (mainClass.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase) ||
         mainClass.Contains("cpw.mods.modlauncher", StringComparison.OrdinalIgnoreCase));

    /// <summary>Legacy 1.12-era Forge/Fabric LaunchWrapper profiles.</summary>
    public static bool UsesLaunchWrapper(string? mainClass) =>
        !string.IsNullOrWhiteSpace(mainClass) &&
        mainClass.Contains("launchwrapper", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves which client jar (if any) belongs on the classpath.
    /// NeoForge must NOT get the inherited vanilla jar — that creates a duplicate
    /// module (e.g. minecraft + _1._21._1) and ResolutionException.
    /// Legacy Forge still needs inheritsFrom jar when the forge profile has no jar.
    /// </summary>
    public static string? ResolveClientJarPath(
        string versionsDir,
        string versionId,
        string? jarProperty,
        string? inheritsFrom,
        string? mainClass,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        var jarId = string.IsNullOrWhiteSpace(jarProperty) ? versionId : jarProperty.Trim();
        var versionJar = Path.Combine(versionsDir, jarId, $"{jarId}.jar");
        if (fileExists(versionJar))
            return versionJar;

        if (UsesBootstrapLauncher(mainClass))
            return null;

        if (string.IsNullOrWhiteSpace(inheritsFrom))
            return null;

        var parentJar = Path.Combine(versionsDir, inheritsFrom, $"{inheritsFrom}.jar");
        return fileExists(parentJar) ? parentJar : null;
    }

    /// <summary>Placeholders every modern/legacy profile may need; missing ones drop args silently.</summary>
    public static IReadOnlyList<string> RequiredSubstitutionKeys { get; } =
    [
        "${natives_directory}",
        "${launcher_name}",
        "${launcher_version}",
        "${version_name}",
        "${library_directory}",
        "${classpath}",
        "${classpath_separator}",
        "${auth_player_name}",
        "${auth_uuid}",
        "${auth_access_token}",
        "${version_type}",
        "${game_directory}",
        "${assets_root}",
        "${assets_index_name}",
        "${user_type}",
        "${clientid}",
        "${auth_xuid}"
    ];

    public static string Substitute(string input, IReadOnlyDictionary<string, string> vars)
    {
        var result = input;
        foreach (var (key, value) in vars)
            result = result.Replace(key, value);
        return result;
    }

    public static bool StillHasPlaceholders(string value) =>
        !string.IsNullOrEmpty(value) && PlaceholderRegex.IsMatch(value);

    public static IReadOnlyList<string> ParseLegacyMinecraftArguments(
        string minecraftArguments,
        IReadOnlyDictionary<string, string> vars)
    {
        var resolved = Substitute(minecraftArguments, vars);
        return resolved
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part) && !StillHasPlaceholders(part))
            .ToList();
    }

    /// <summary>Remove flags whose value was dropped (e.g. bare -p after unsubstituted module path).</summary>
    public static void DropOrphanValueFlags(IList<string> args)
    {
        for (var i = args.Count - 1; i >= 0; i--)
        {
            var arg = args[i];
            if (arg is not ("-p" or "--module-path" or "-cp" or "--class-path" or "--add-opens"
                or "--add-exports" or "--add-modules"))
                continue;

            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith('-');
            if (!hasValue)
                args.RemoveAt(i);
        }
    }
}
