using System;
using System.Collections.Generic;

namespace Apeiron.Services;

public static class JavaVersionHelper
{
    /// <summary>
    /// Minecraft → Oracle JDK mapping:
    /// <list type="table">
    ///   <item><term>26.x (year releases)</term><description>Java 25</description></item>
    ///   <item><term>1.20.5 – 1.21.x</term><description>Java 21</description></item>
    ///   <item><term>1.18 – 1.20.4</term><description>Java 17 (up to 21)</description></item>
    ///   <item><term>1.17.x</term><description>Java 17</description></item>
    ///   <item><term>1.16.x and below</term><description>Java 8</description></item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<(string Minecraft, int MinJava, int MaxJava, int InstallJava)> GetMinecraftJavaMappings() =>
    [
        ("26.x (year releases)", 25, 25, 25),
        ("1.20.5 – 1.21.x", 21, 21, 21),
        ("1.18 – 1.20.4", 17, 21, 21),
        ("1.17.x", 17, 17, 17),
        ("1.16.x and below", 8, 8, 8)
    ];

    public static int GetRequiredJavaMajor(string mcVersion)
    {
        if (!TryParseMcVersion(mcVersion, out var parsed))
            return 21;

        if (parsed.IsYearRelease)
            return 25;

        if (parsed.Minor <= 16)
            return 8;

        if (parsed.Minor == 17)
            return 17;

        if (parsed.Minor < 20)
            return 17;

        if (parsed.Minor == 20 && parsed.Patch < 5)
            return 17;

        return 21;
    }

    public static int GetMaxJavaMajor(string mcVersion)
    {
        if (!TryParseMcVersion(mcVersion, out var parsed))
            return 21;

        if (parsed.IsYearRelease)
            return 25;

        if (parsed.Minor <= 16)
            return 8;

        if (parsed.Minor == 17)
            return 17;

        if (parsed.Minor < 20)
            return 21;

        if (parsed.Minor == 20 && parsed.Patch < 5)
            return 21;

        if (parsed.Minor <= 21)
            return 21;

        return 25;
    }

    public static int GetPreferredJavaMajor(string mcVersion)
    {
        var min = GetRequiredJavaMajor(mcVersion);
        var max = GetMaxJavaMajor(mcVersion);
        return max >= min ? max : min;
    }

    public static string GetRequiredJavaLabel(string mcVersion)
    {
        var min = GetRequiredJavaMajor(mcVersion);
        var max = GetMaxJavaMajor(mcVersion);
        return min == max ? $"Java {min}" : $"Java {min}–{max}";
    }

    public static bool SupportsSunMiscUnsafeAccess(int javaMajor) => javaMajor >= 24;

    private static bool TryParseMcVersion(string mcVersion, out McVersion parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(mcVersion))
            return false;

        var parts = mcVersion.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var head))
            return false;

        if (head >= 26)
        {
            parsed = new McVersion(IsYearRelease: true, Minor: 0, Patch: 0);
            return true;
        }

        if (head >= 20 && parts[0] != "1")
        {
            parsed = new McVersion(IsYearRelease: false, Minor: head, Patch: 0);
            return true;
        }

        if (head != 1 || parts.Length < 2 || !int.TryParse(parts[1], out var minor))
            return false;

        var patch = 0;
        if (parts.Length >= 3)
        {
            var patchPart = parts[2];
            var dash = patchPart.IndexOf('-', StringComparison.Ordinal);
            if (dash >= 0)
                patchPart = patchPart[..dash];

            int.TryParse(patchPart, out patch);
        }

        parsed = new McVersion(IsYearRelease: false, Minor: minor, Patch: patch);
        return true;
    }

    private readonly record struct McVersion(bool IsYearRelease, int Minor, int Patch);
}
