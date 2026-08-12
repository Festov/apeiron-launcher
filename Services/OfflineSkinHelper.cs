using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Apeiron.Services;

/// <summary>
/// Best-effort offline skin for the game: resource pack + CustomSkinLoader local files when present.
/// Vanilla offline still cannot load Microsoft skins; this covers CSL and default steve/alex overrides.
/// </summary>
public static class OfflineSkinHelper
{
    public const string ResourcePackFileName = "Apeiron-Offline-Skin.zip";

    public static void ApplyForOfflineLaunch(
        BuildInfo build,
        SettingsService settings,
        SkinService skins,
        Action<string>? log = null)
    {
        try
        {
            var skinPath = ResolveSkinFile(settings, skins);
            if (skinPath == null || !File.Exists(skinPath))
            {
                log?.Invoke(LocalizationService.T("offline_skin.none"));
                return;
            }

            var gameDir = build.GetGameDir();
            Directory.CreateDirectory(gameDir);

            var isSlim = string.Equals(settings.SkinModel, "slim", StringComparison.OrdinalIgnoreCase) ||
                         SkinService.IsSlimStandard(settings.SkinPreset);

            WriteResourcePack(gameDir, skinPath, isSlim);
            EnableResourcePack(gameDir);
            WriteCustomSkinLoaderFiles(gameDir, settings.OfflineUsername, skinPath, isSlim);

            log?.Invoke(LocalizationService.T("offline_skin.applied"));
        }
        catch (Exception ex)
        {
            log?.Invoke(LocalizationService.F("offline_skin.error", ex.Message));
        }
    }

    private static string? ResolveSkinFile(SettingsService settings, SkinService skins)
    {
        if (string.Equals(settings.SkinPreset, "custom", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(settings.CustomSkinPath) &&
            File.Exists(settings.CustomSkinPath))
            return settings.CustomSkinPath;

        if (SkinService.IsStandardSkinId(settings.SkinPreset))
            return skins.GetStandardSkinPath(settings.SkinPreset);

        if (!string.IsNullOrWhiteSpace(settings.CustomSkinPath) && File.Exists(settings.CustomSkinPath))
            return settings.CustomSkinPath;

        return skins.GetStandardSkinPath("steve");
    }

    private static void WriteResourcePack(string gameDir, string skinPath, bool slim)
    {
        var packsDir = Path.Combine(gameDir, "resourcepacks");
        Directory.CreateDirectory(packsDir);
        var zipPath = Path.Combine(packsDir, ResourcePackFileName);
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        var bytes = File.ReadAllBytes(skinPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var meta = """
            {
              "pack": {
                "pack_format": 34,
                "description": "Apeiron offline skin"
              }
            }
            """;
        WriteZipEntry(zip, "pack.mcmeta", Encoding.UTF8.GetBytes(meta.Replace("\r\n", "\n")));

        // Cover classic + slim default player textures used when offline.
        WriteZipEntry(zip, "assets/minecraft/textures/entity/player/wide/steve.png", bytes);
        WriteZipEntry(zip, "assets/minecraft/textures/entity/player/slim/alex.png", bytes);
        WriteZipEntry(zip, "assets/minecraft/textures/entity/steve.png", bytes);
        WriteZipEntry(zip, "assets/minecraft/textures/entity/alex.png", bytes);
        if (slim)
            WriteZipEntry(zip, "assets/minecraft/textures/entity/player/slim/steve.png", bytes);
        else
            WriteZipEntry(zip, "assets/minecraft/textures/entity/player/wide/alex.png", bytes);
    }

    private static void WriteZipEntry(ZipArchive zip, string entryName, byte[] data)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data, 0, data.Length);
    }

    private static void EnableResourcePack(string gameDir)
    {
        var optionsPath = Path.Combine(gameDir, "options.txt");
        var lines = File.Exists(optionsPath)
            ? File.ReadAllLines(optionsPath).ToList()
            : new List<string>();

        var packRef = $"file/{ResourcePackFileName}";
        var updated = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("resourcePacks:", StringComparison.Ordinal))
                continue;

            var list = ParseResourcePackList(lines[i]);
            if (!list.Any(p => p.Contains("Apeiron-Offline-Skin", StringComparison.OrdinalIgnoreCase)))
                list.Insert(0, packRef);
            lines[i] = "resourcePacks:" + FormatResourcePackList(list);
            updated = true;
            break;
        }

        if (!updated)
            lines.Add("resourcePacks:" + FormatResourcePackList(new List<string> { packRef }));

        File.WriteAllLines(optionsPath, lines);
    }

    private static List<string> ParseResourcePackList(string line)
    {
        var idx = line.IndexOf(':');
        var raw = idx >= 0 ? line[(idx + 1)..].Trim() : "[]";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string FormatResourcePackList(List<string> packs) =>
        "[" + string.Join(",", packs.Select(p => "\"" + p.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]";

    private static void WriteCustomSkinLoaderFiles(string gameDir, string username, string skinPath, bool slim)
    {
        var modsDir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(modsDir))
            return;

        var hasCsl = Directory.EnumerateFiles(modsDir, "*.jar")
            .Any(f => Path.GetFileName(f).Contains("customskinloader", StringComparison.OrdinalIgnoreCase) ||
                      Path.GetFileName(f).Contains("CustomSkinLoader", StringComparison.OrdinalIgnoreCase));
        if (!hasCsl)
            return;

        var safeName = SanitizeUsername(username);
        var localRoot = Path.Combine(gameDir, "CustomSkinLoader", "LocalSkin");
        Directory.CreateDirectory(Path.Combine(localRoot, "skins"));
        var dest = Path.Combine(localRoot, "skins", safeName + ".png");
        File.Copy(skinPath, dest, overwrite: true);

        // Also common legacy layout
        Directory.CreateDirectory(localRoot);
        File.Copy(skinPath, Path.Combine(localRoot, safeName + ".png"), overwrite: true);

        var configDir = Path.Combine(gameDir, "CustomSkinLoader");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "CustomSkinLoader.json");
        if (!File.Exists(configPath))
        {
            var model = slim ? "slim" : "default";
            var json = $$"""
                {
                  "version": "14.15",
                  "buildNumber": 0,
                  "loadlist": [
                    {
                      "name": "LocalSkin",
                      "type": "Legacy",
                      "checkPNG": false,
                      "skin": "LocalSkin/skins/{USERNAME}.png",
                      "model": "{{model}}",
                      "cape": "LocalSkin/capes/{USERNAME}.png"
                    }
                  ]
                }
                """;
            File.WriteAllText(configPath, json.Replace("\r\n", "\n"));
        }
    }

    private static string SanitizeUsername(string username)
    {
        var cleaned = Regex.Replace(username ?? "", @"[^a-zA-Z0-9_]", "");
        return string.IsNullOrWhiteSpace(cleaned) ? OfflineUsernameHelper.Default : cleaned;
    }
}
