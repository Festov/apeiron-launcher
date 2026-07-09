using System;
using System.IO;
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
    public string Language { get; set; } = "auto";
    public bool OfflineOnly { get; set; }
    public bool CheckForUpdates { get; set; } = true;

    public SettingsService()
    {
        _launcherDir = AppDomain.CurrentDomain.BaseDirectory;
        var configDir = Path.Combine(_launcherDir, "config");
        Directory.CreateDirectory(configDir);
        _settingsPath = Path.Combine(configDir, "settings.json");
    }

    public string GetDefaultMinecraftDir() =>
        Path.Combine(_launcherDir, ".minecraft");

    public string GetMinecraftDir() => GetDefaultMinecraftDir();

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
            if (root.TryGetProperty("Language", out var lang))
                Language = lang.GetString() ?? "auto";
            if (root.TryGetProperty("OfflineOnly", out var offlineOnly))
                OfflineOnly = offlineOnly.GetBoolean();
            if (root.TryGetProperty("CheckForUpdates", out var checkUpdates))
                CheckForUpdates = checkUpdates.GetBoolean();

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
                Language,
                OfflineOnly,
                CheckForUpdates
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("log.settings.save_error", ex.Message));
        }
    }
}
