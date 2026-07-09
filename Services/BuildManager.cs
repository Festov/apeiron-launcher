using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Apeiron.Services;

public class BuildManager
{
    private readonly string _launcherDir;
    private readonly string _configPath;
    private List<BuildInfo> _builds = new();

    public BuildManager()
    {
        _launcherDir = AppDomain.CurrentDomain.BaseDirectory;
        var configDir = Path.Combine(_launcherDir, "config");
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, "builds.json");
    }

    public List<BuildInfo> LoadBuilds()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _builds = JsonSerializer.Deserialize<List<BuildInfo>>(json) ?? new List<BuildInfo>();
                var migrated = false;

                foreach (var build in _builds)
                {
                    if (MigrateBuild(build))
                        migrated = true;
                }

                if (migrated)
                    SaveBuilds(_builds);

                return _builds;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("log.builds.load_error", ex.Message));
        }
        return new List<BuildInfo>();
    }

    private bool MigrateBuild(BuildInfo build)
    {
        var changed = false;

        if (string.IsNullOrEmpty(build.Id))
        {
            build.Id = Guid.NewGuid().ToString();
            changed = true;
        }

        if (string.IsNullOrEmpty(build.InstancePath))
        {
            build.InstancePath = Path.Combine(_launcherDir, "instances", build.Id);
            changed = true;
        }

        build.EnsureInstanceFolders();
        return changed;
    }

    public void SaveBuilds(List<BuildInfo> builds)
    {
        try
        {
            _builds = builds;
            var json = JsonSerializer.Serialize(builds, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine(LocalizationService.F("log.builds.save_error", ex.Message));
        }
    }

    public void AddBuild(BuildInfo build)
    {
        if (build == null) return;

        if (string.IsNullOrEmpty(build.Id))
            build.Id = Guid.NewGuid().ToString();

        if (string.IsNullOrEmpty(build.InstancePath))
            build.InstancePath = Path.Combine(_launcherDir, "instances", build.Id);

        build.EnsureInstanceFolders();

        if (_builds.Any(b => b.Name.Equals(build.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(LocalizationService.F("build.duplicate_exists", build.Name));
        }

        _builds.Add(build);
        SaveBuilds(_builds);
    }

    public void UpdateBuild(BuildInfo updated)
    {
        var index = _builds.FindIndex(b => b.Id == updated.Id);
        if (index < 0)
            throw new InvalidOperationException(LocalizationService.T("build.not_found"));

        if (_builds.Any(b => b.Id != updated.Id && b.Name.Equals(updated.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(LocalizationService.F("build.duplicate_exists", updated.Name));

        _builds[index] = updated;
        SaveBuilds(_builds);
    }

    public void RemoveBuildById(string id)
    {
        var build = _builds.FirstOrDefault(b => b.Id == id);
        if (build == null) return;

        DeleteInstanceFolder(build);
        _builds.RemoveAll(b => b.Id == id);
        SaveBuilds(_builds);
    }

    private void DeleteInstanceFolder(BuildInfo build)
    {
        var path = build.GetGameDir();
        if (!Directory.Exists(path)) return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(LocalizationService.F("build.delete_folder_failed", ex.Message), ex);
        }
    }

    public BuildInfo DuplicateBuild(string sourceId, bool copyModsAndConfig)
    {
        var source = _builds.FirstOrDefault(b => b.Id == sourceId)
            ?? throw new InvalidOperationException(LocalizationService.T("build.not_found"));

        var name = GenerateUniqueName($"{source.Name}{LocalizationService.T("build.copy_suffix")}");
        var clone = source.CloneWithNewId(name);
        clone.InstancePath = Path.Combine(_launcherDir, "instances", clone.Id);
        clone.EnsureInstanceFolders();

        if (copyModsAndConfig)
        {
            var sourceDir = source.GetGameDir();
            if (Directory.Exists(sourceDir))
                CopyInstanceData(sourceDir, clone.GetGameDir());
        }

        _builds.Add(clone);
        SaveBuilds(_builds);
        return clone;
    }

    private string GenerateUniqueName(string baseName)
    {
        if (!_builds.Any(b => b.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!_builds.Any(b => b.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{baseName} {Guid.NewGuid().ToString()[..8]}";
    }

    private static void CopyInstanceData(string sourceDir, string targetDir)
    {
        foreach (var folder in new[] { "mods", "config", "resourcepacks", "shaderpacks" })
        {
            var src = Path.Combine(sourceDir, folder);
            if (!Directory.Exists(src)) continue;
            CopyDirectoryRecursive(src, Path.Combine(targetDir, folder));
        }
    }

    private static void CopyDirectoryRecursive(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
