using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Apeiron.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly string _launcherDir;

    public int Ram { get; set; } = 8;
    public bool DarkTheme { get; set; } = true;
    public string OfflineUsername { get; set; } = OfflineUsernameHelper.Default;
    public string DefaultBuildId { get; set; } = "";
    public string LastPlayedBuildId { get; set; } = "";
    public string Language { get; set; } = "auto";
    public bool OfflineOnly { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public string SkinPreset { get; set; } = "default";
    public string SkinModel { get; set; } = "classic";
    public string CustomSkinPath { get; set; } = "";
    /// <summary>Hex background for 3D skin preview, e.g. #07090D.</summary>
    public string SkinPreviewBackground { get; set; } = "#07090D";
    /// <summary>Empty = default .minecraft next to launcher.</summary>
    public string MinecraftDir { get; set; } = "";
    public bool MinimizeToTray { get; set; }
    public bool CloseOnGameLaunch { get; set; }
    public string JvmProfile { get; set; } = JvmProfileHelper.Default;
    public bool OnboardingCompleted { get; set; }
    public int AssetParallelism { get; set; } = 24;
    public string CurseForgeApiKey { get; set; } =
        "$2a$10$ch9m5Af.9tnalNjC18wLvuhnD0O38uL6ufQ.OiD22qed0Cu8mCva6";
    public List<string> RecentMcVersions { get; set; } = new();

    public SettingsService(string? launcherDir = null)
    {
        _launcherDir = launcherDir ?? AppDomain.CurrentDomain.BaseDirectory;
        var configDir = Path.Combine(_launcherDir, "config");
        Directory.CreateDirectory(configDir);
        _settingsPath = Path.Combine(configDir, "settings.json");
    }

    public string GetDefaultMinecraftDir() =>
        Path.Combine(_launcherDir, ".minecraft");

    public string GetMinecraftDir()
    {
        if (string.IsNullOrWhiteSpace(MinecraftDir))
            return GetDefaultMinecraftDir();

        try
        {
            return Path.GetFullPath(MinecraftDir.Trim());
        }
        catch
        {
            return GetDefaultMinecraftDir();
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;

            var json = File.ReadAllText(_settingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Ram", out var ram))
                Ram = ram.GetInt32();
            if (root.TryGetProperty("DarkTheme", out var theme))
                DarkTheme = theme.GetBoolean();
            if (root.TryGetProperty("OfflineUsername", out var offline))
                OfflineUsername = OfflineUsernameHelper.Sanitize(offline.GetString());
            if (root.TryGetProperty("DefaultBuildId", out var defaultBuild))
                DefaultBuildId = defaultBuild.GetString() ?? "";
            if (root.TryGetProperty("LastPlayedBuildId", out var lastPlayed))
                LastPlayedBuildId = lastPlayed.GetString() ?? "";
            if (root.TryGetProperty("Language", out var lang))
                Language = lang.GetString() ?? "auto";
            if (root.TryGetProperty("OfflineOnly", out var offlineOnly))
                OfflineOnly = offlineOnly.GetBoolean();
            if (root.TryGetProperty("CheckForUpdates", out var checkUpdates))
                CheckForUpdates = checkUpdates.GetBoolean();
            if (root.TryGetProperty("SkinPreset", out var skinPreset))
                SkinPreset = skinPreset.GetString() ?? "default";
            if (root.TryGetProperty("SkinModel", out var skinModel))
                SkinModel = skinModel.GetString() ?? "classic";
            if (root.TryGetProperty("CustomSkinPath", out var customSkin))
                CustomSkinPath = customSkin.GetString() ?? "";
            if (root.TryGetProperty("SkinPreviewBackground", out var skinBg))
                SkinPreviewBackground = NormalizeHexColor(skinBg.GetString(), "#07090D");
            if (root.TryGetProperty("MinecraftDir", out var minecraftDir))
                MinecraftDir = minecraftDir.GetString() ?? "";
            if (root.TryGetProperty("MinimizeToTray", out var tray))
                MinimizeToTray = tray.GetBoolean();
            if (root.TryGetProperty("CloseOnGameLaunch", out var closeOnLaunch))
                CloseOnGameLaunch = closeOnLaunch.GetBoolean();
            if (root.TryGetProperty("JvmProfile", out var jvmProfile))
                JvmProfile = jvmProfile.GetString() ?? JvmProfileHelper.Default;
            if (root.TryGetProperty("OnboardingCompleted", out var onboarding))
                OnboardingCompleted = onboarding.GetBoolean();
            if (root.TryGetProperty("AssetParallelism", out var parallelism) &&
                parallelism.TryGetInt32(out var p))
                AssetParallelism = Math.Clamp(p, 4, 64);
            if (root.TryGetProperty("CurseForgeApiKey", out var curseForgeKey))
            {
                var key = curseForgeKey.GetString();
                if (!string.IsNullOrWhiteSpace(key))
                    CurseForgeApiKey = key;
            }
            if (root.TryGetProperty("RecentMcVersions", out var recentMcVersions) &&
                recentMcVersions.ValueKind == JsonValueKind.Array)
            {
                RecentMcVersions = recentMcVersions
                    .EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(version => !string.IsNullOrWhiteSpace(version))
                    .Select(version => version!.Trim())
                    .ToList();
            }

            if (Ram == 4)
            {
                Ram = 8;
                Save();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("log.settings.load_error", ex.Message));
        }
    }

    public void Save()
    {
        try
        {
            var data = new
            {
                Ram,
                DarkTheme,
                OfflineUsername = OfflineUsernameHelper.Sanitize(OfflineUsername),
                DefaultBuildId,
                LastPlayedBuildId,
                Language,
                OfflineOnly,
                CheckForUpdates,
                SkinPreset,
                SkinModel,
                CustomSkinPath,
                SkinPreviewBackground,
                MinecraftDir,
                MinimizeToTray,
                CloseOnGameLaunch,
                JvmProfile,
                OnboardingCompleted,
                AssetParallelism,
                CurseForgeApiKey,
                RecentMcVersions
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("log.settings.save_error", ex.Message));
        }
    }

    public static string NormalizeHexColor(string? value, string fallback = "#07090D")
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var t = value.Trim();
        if (!t.StartsWith('#') && t.Length == 6)
            t = "#" + t;
        if (t.Length != 7 || t[0] != '#')
            return fallback;
        for (var i = 1; i < 7; i++)
        {
            var c = t[i];
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok)
                return fallback;
        }
        return t.ToUpperInvariant();
    }
}
