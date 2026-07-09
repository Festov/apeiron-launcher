using System.IO.Compression;
using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class BuildExportServiceTests
{
    [Fact]
    public void Export_includes_metadata_and_mod_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-export-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(root, "export.zip");

        try
        {
            var build = new BuildInfo
            {
                Name = "Test Build",
                MinecraftVersion = "1.20.1",
                Loader = "Fabric",
                LoaderVersion = "0.15.0",
                IsModded = true,
                InstancePath = Path.Combine(root, "instance")
            };

            build.EnsureInstanceFolders();
            File.WriteAllText(Path.Combine(build.GetModsDir(), "demo.jar"), "demo");
            Directory.CreateDirectory(root);

            BuildExportService.Export(build, zipPath);

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.NotNull(archive.GetEntry("build-export.json"));
            Assert.NotNull(archive.GetEntry("mods/demo.jar"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Import_roundtrip_restores_metadata_and_mods()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-import-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(root, "export.zip");
        var instancesRoot = Path.Combine(root, "instances");

        try
        {
            var source = new BuildInfo
            {
                Name = "Roundtrip",
                MinecraftVersion = "1.20.1",
                Loader = "Fabric",
                LoaderVersion = "0.15.0",
                IsModded = true,
                InstancePath = Path.Combine(root, "source-instance")
            };

            source.EnsureInstanceFolders();
            File.WriteAllText(Path.Combine(source.GetModsDir(), "demo.jar"), "demo");
            Directory.CreateDirectory(instancesRoot);

            BuildExportService.Export(source, zipPath);

            var imported = BuildExportService.Import(zipPath, instancesRoot);

            Assert.Equal("Roundtrip", imported.Name);
            Assert.Equal("1.20.1", imported.MinecraftVersion);
            Assert.Equal("Fabric", imported.Loader);
            Assert.True(File.Exists(Path.Combine(imported.GetModsDir(), "demo.jar")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ImportInto_copies_folders_into_existing_build()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-import-into-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(root, "export.zip");

        try
        {
            var source = new BuildInfo
            {
                Name = "Source",
                InstancePath = Path.Combine(root, "source")
            };
            source.EnsureInstanceFolders();
            File.WriteAllText(Path.Combine(source.GetModsDir(), "demo.jar"), "demo");
            Directory.CreateDirectory(Path.Combine(source.GetGameDir(), "config"));
            File.WriteAllText(Path.Combine(source.GetGameDir(), "config", "demo.cfg"), "cfg");

            BuildExportService.Export(source, zipPath);

            var target = new BuildInfo { Name = "Target", InstancePath = Path.Combine(root, "target") };
            BuildExportService.ImportInto(zipPath, target);

            Assert.True(File.Exists(Path.Combine(target.GetModsDir(), "demo.jar")));
            Assert.True(File.Exists(Path.Combine(target.GetGameDir(), "config", "demo.cfg")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void FullBackup_includes_saves_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-backup-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(root, "backup.zip");

        try
        {
            var build = new BuildInfo
            {
                Name = "Backup",
                InstancePath = Path.Combine(root, "instance")
            };

            build.EnsureInstanceFolders();
            Directory.CreateDirectory(Path.Combine(build.GetGameDir(), "saves", "World1"));
            File.WriteAllText(Path.Combine(build.GetGameDir(), "saves", "World1", "level.dat"), "world");
            Directory.CreateDirectory(root);

            BuildExportService.Export(build, zipPath, BuildExportMode.FullBackup);

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.NotNull(archive.GetEntry("saves/World1/level.dat"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Import_applies_overrides_folder_from_modpack_zip()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-overrides-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(root, "modpack.zip");
        var instancesRoot = Path.Combine(root, "instances");

        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(instancesRoot);

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("overrides/config/demo.cfg").Open());
                writer.Write("cfg");
            }

            var imported = BuildExportService.Import(zipPath, instancesRoot);
            Assert.True(File.Exists(Path.Combine(imported.GetGameDir(), "config", "demo.cfg")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
