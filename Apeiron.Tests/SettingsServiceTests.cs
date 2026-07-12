using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class SettingsServiceTests
{
    private static string CreateLauncherRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void Save_and_Load_roundtrip()
    {
        var root = CreateLauncherRoot();

        try
        {
            var settings = new SettingsService(root)
            {
                Ram = 12,
                DarkTheme = false,
                OfflineUsername = "Player",
                DefaultBuildId = "build-1",
                Language = "ru",
                OfflineOnly = true,
                CheckForUpdates = false
            };
            settings.Save();

            var loaded = new SettingsService(root);
            loaded.Load();

            Assert.Equal(12, loaded.Ram);
            Assert.False(loaded.DarkTheme);
            Assert.Equal("Player", loaded.OfflineUsername);
            Assert.Equal("build-1", loaded.DefaultBuildId);
            Assert.Equal("ru", loaded.Language);
            Assert.True(loaded.OfflineOnly);
            Assert.False(loaded.CheckForUpdates);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Load_migrates_legacy_ram_4_to_8()
    {
        var root = CreateLauncherRoot();
        var configDir = Path.Combine(root, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(
            Path.Combine(configDir, "settings.json"),
            """{"Ram":4,"DarkTheme":true,"OfflineUsername":"Player","Language":"auto"}""");

        try
        {
            var settings = new SettingsService(root);
            settings.Load();

            Assert.Equal(8, settings.Ram);

            var reloaded = new SettingsService(root);
            reloaded.Load();
            Assert.Equal(8, reloaded.Ram);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Load_sanitizes_offline_username()
    {
        var root = CreateLauncherRoot();
        var configDir = Path.Combine(root, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(
            Path.Combine(configDir, "settings.json"),
            """{"Ram":8,"DarkTheme":true,"OfflineUsername":"Иван","Language":"auto"}""");

        try
        {
            var settings = new SettingsService(root);
            settings.Load();

            Assert.Equal(OfflineUsernameHelper.Default, settings.OfflineUsername);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void GetMinecraftDir_points_under_launcher_root()
    {
        var root = CreateLauncherRoot();

        try
        {
            var settings = new SettingsService(root);
            Assert.Equal(Path.Combine(root, ".minecraft"), settings.GetMinecraftDir());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
