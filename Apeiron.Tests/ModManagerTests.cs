using System.IO.Compression;
using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class ModManagerTests
{
    private static string CreateModsDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "apeiron-mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFabricModJar(string path, string name, string version)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("fabric.mod.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write($$"""{"schemaVersion":1,"id":"demo","name":"{{name}}","version":"{{version}}"}""");
    }

    private static void WriteForgeModJar(string path, string displayName, string version)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("META-INF/mods.toml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(
            $$"""
            modLoader="javafml"
            loaderVersion="[47,)"
            [[mods]]
            modId="demo"
            displayName="{{displayName}}"
            version="{{version}}"
            """);
    }

    [Fact]
    public void ReadModMetadata_reads_fabric_mod_json()
    {
        var root = CreateModsDir();
        var jar = Path.Combine(root, "fabric-mod.jar");

        try
        {
            WriteFabricModJar(jar, "Fabric Demo", "2.1.0");
            var meta = ModManager.ReadModMetadata(jar);

            Assert.Equal("Fabric Demo", meta.DisplayName);
            Assert.Equal("2.1.0", meta.Version);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ReadModMetadata_reads_forge_mods_toml()
    {
        var root = CreateModsDir();
        var jar = Path.Combine(root, "forge-mod.jar");

        try
        {
            WriteForgeModJar(jar, "Forge Demo", "3.0.0");
            var meta = ModManager.ReadModMetadata(jar);

            Assert.Equal("Forge Demo", meta.DisplayName);
            Assert.Equal("3.0.0", meta.Version);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ListMods_detects_disabled_mods()
    {
        var root = CreateModsDir();
        var disabled = Path.Combine(root, "demo.jar.disabled");

        try
        {
            WriteFabricModJar(disabled, "Disabled Mod", "1.0.0");
            var mods = ModManager.ListMods(root);

            Assert.Single(mods);
            Assert.False(mods[0].IsEnabled);
            Assert.Equal("Disabled Mod", mods[0].DisplayName);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SetModEnabled_toggles_disabled_suffix()
    {
        var root = CreateModsDir();
        var jar = Path.Combine(root, "toggle.jar");

        try
        {
            WriteFabricModJar(jar, "Toggle Mod", "1.0.0");
            var mods = ModManager.ListMods(root);
            var mod = mods.Single();

            ModManager.SetModEnabled(mod, enabled: false);
            Assert.False(File.Exists(jar));
            Assert.True(File.Exists(jar + ".disabled"));

            ModManager.SetModEnabled(mod, enabled: true);
            Assert.True(File.Exists(jar));
            Assert.False(File.Exists(jar + ".disabled"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ImportMods_copies_jar_files()
    {
        var root = CreateModsDir();
        var sourceDir = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceJar = Path.Combine(sourceDir, "imported.jar");

        try
        {
            WriteFabricModJar(sourceJar, "Imported", "1.0.0");
            var count = ModManager.ImportMods(root, new[] { sourceJar, Path.Combine(sourceDir, "readme.txt") });

            Assert.Equal(1, count);
            Assert.True(File.Exists(Path.Combine(root, "imported.jar")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
