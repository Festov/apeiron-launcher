using System.Text.Json;
using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class BuildManagerTests
{
    private static string CreateLauncherRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-buildmgr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static BuildInfo CreateBuild(string name, string? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name,
            MinecraftVersion = "1.20.1",
            Loader = "Fabric",
            LoaderVersion = "0.15.0",
            IsModded = true
        };

    [Fact]
    public void LoadBuilds_migrates_missing_id_and_instance_path()
    {
        var root = CreateLauncherRoot();
        var configDir = Path.Combine(root, "config");
        Directory.CreateDirectory(configDir);

        var legacyBuild = new { Name = "Legacy", MinecraftVersion = "1.20.1", Loader = "", LoaderVersion = "", IsModded = false };
        var json = JsonSerializer.Serialize(new[] { legacyBuild });
        File.WriteAllText(Path.Combine(configDir, "builds.json"), json);

        try
        {
            var manager = new BuildManager(root);
            var builds = manager.LoadBuilds();

            Assert.Single(builds);
            Assert.False(string.IsNullOrEmpty(builds[0].Id));
            Assert.Equal(Path.Combine(root, "instances", builds[0].Id), builds[0].InstancePath);
            Assert.True(Directory.Exists(Path.Combine(builds[0].InstancePath, "mods")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void AddBuild_rejects_duplicate_names()
    {
        var root = CreateLauncherRoot();

        try
        {
            var manager = new BuildManager(root);
            manager.LoadBuilds();
            manager.AddBuild(CreateBuild("Same Name"));

            var duplicate = CreateBuild("same name");
            Assert.Throws<InvalidOperationException>(() => manager.AddBuild(duplicate));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void DuplicateBuild_copies_mods_when_requested()
    {
        var root = CreateLauncherRoot();

        try
        {
            var manager = new BuildManager(root);
            manager.LoadBuilds();

            var source = CreateBuild("Source");
            manager.AddBuild(source);

            var modPath = Path.Combine(source.GetGameDir(), "mods", "demo.jar");
            File.WriteAllText(modPath, "demo");

            var clone = manager.DuplicateBuild(source.Id, copyModsAndConfig: true);

            Assert.NotEqual(source.Id, clone.Id);
            Assert.True(File.Exists(Path.Combine(clone.GetGameDir(), "mods", "demo.jar")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void RemoveBuildById_deletes_instance_folder()
    {
        var root = CreateLauncherRoot();

        try
        {
            var manager = new BuildManager(root);
            manager.LoadBuilds();

            var build = CreateBuild("ToRemove");
            manager.AddBuild(build);
            var instanceDir = build.GetGameDir();
            Assert.True(Directory.Exists(instanceDir));

            manager.RemoveBuildById(build.Id);

            Assert.False(Directory.Exists(instanceDir));
            Assert.Empty(manager.LoadBuilds());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void LoadBuilds_backups_corrupt_config()
    {
        var root = CreateLauncherRoot();
        var configDir = Path.Combine(root, "config");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "builds.json");
        File.WriteAllText(configPath, "{ not valid json");

        try
        {
            var manager = new BuildManager(root);
            Assert.Throws<InvalidOperationException>(() => manager.LoadBuilds());
            Assert.True(Directory.GetFiles(configDir, "builds.json.corrupt.*.bak").Length >= 1);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
