using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Apeiron.Services;

public class ModManager
{
    public class ModEntry
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; init; } = "";
        public bool IsEnabled { get; set; }
        public string DisplayName { get; set; } = "";
        public string ModVersion { get; set; } = "";

        public string ListLabel =>
            string.IsNullOrWhiteSpace(ModVersion)
                ? DisplayName
                : $"{DisplayName} ({ModVersion})";
    }

    public static List<ModEntry> ListMods(string modsDir)
    {
        Directory.CreateDirectory(modsDir);
        var result = new List<ModEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(modsDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                continue;

            var baseName = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? name[..^".disabled".Length]
                : name;

            if (!seen.Add(baseName)) continue;

            var enabledPath = Path.Combine(modsDir, baseName);
            var disabledPath = enabledPath + ".disabled";
            var isEnabled = File.Exists(enabledPath);
            var jarPath = isEnabled ? enabledPath : disabledPath;
            var meta = ReadModMetadata(jarPath);

            result.Add(new ModEntry
            {
                FilePath = jarPath,
                FileName = baseName,
                IsEnabled = isEnabled,
                DisplayName = meta.DisplayName,
                ModVersion = meta.Version
            });
        }

        return result.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static (string DisplayName, string Version) ReadModMetadata(string jarPath)
    {
        var fileName = Path.GetFileName(jarPath);
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);

            var fabric = zip.GetEntry("fabric.mod.json");
            if (fabric != null)
            {
                using var reader = new StreamReader(fabric.Open());
                using var doc = JsonDocument.Parse(reader.ReadToEnd());
                var root = doc.RootElement;
                var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
                return (name ?? fileName, version ?? "");
            }

            var forge = zip.GetEntry("META-INF/mods.toml");
            if (forge != null)
            {
                using var reader = new StreamReader(forge.Open());
                var text = reader.ReadToEnd();
                var displayName = MatchTomlValue(text, "displayName") ?? MatchTomlValue(text, "modId") ?? fileName;
                var version = MatchTomlValue(text, "version") ?? "";
                return (displayName, version);
            }
        }
        catch { }

        return (fileName, "");
    }

    private static string? MatchTomlValue(string text, string key)
    {
        var match = Regex.Match(text, $@"(?<![\w]){Regex.Escape(key)}\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static int ImportMods(string modsDir, IEnumerable<string> sourceFiles)
    {
        Directory.CreateDirectory(modsDir);
        var imported = 0;

        foreach (var source in sourceFiles)
        {
            if (!source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileName(source);
            var dest = Path.Combine(modsDir, fileName);
            File.Copy(source, dest, overwrite: true);
            imported++;
        }

        return imported;
    }

    public static void SetModEnabled(ModEntry mod, bool enabled)
    {
        var modsDir = Path.GetDirectoryName(mod.FilePath)!;
        var enabledPath = Path.Combine(modsDir, mod.FileName);
        var disabledPath = enabledPath + ".disabled";

        if (enabled)
        {
            if (File.Exists(disabledPath))
            {
                if (File.Exists(enabledPath)) File.Delete(enabledPath);
                File.Move(disabledPath, enabledPath);
            }
            mod.FilePath = enabledPath;
        }
        else
        {
            if (File.Exists(enabledPath))
            {
                if (File.Exists(disabledPath)) File.Delete(disabledPath);
                File.Move(enabledPath, disabledPath);
            }
            mod.FilePath = disabledPath;
        }

        mod.IsEnabled = enabled;
    }

    public static void ApplyModStates(string modsDir, IEnumerable<ModEntry> mods)
    {
        foreach (var mod in mods)
            SetModEnabled(mod, mod.IsEnabled);
    }

    public static void SetAllModsEnabled(string modsDir, bool enabled)
    {
        foreach (var mod in ListMods(modsDir))
            SetModEnabled(mod, enabled);
    }
}