using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;

namespace Apeiron.Services;

public static class BuildExportService
{
    private static readonly string[] ModpackFolders = ["mods", "config", "resourcepacks", "shaderpacks"];
    private static readonly string[] FullBackupExtraFolders = ["saves", "defaultconfigs"];
    private static readonly string[] RootFiles = ["options.txt", "servers.dat"];

    public static void Export(BuildInfo build, string zipPath, BuildExportMode mode = BuildExportMode.Modpack)
    {
        var gameDir = build.GetGameDir();
        build.EnsureInstanceFolders();
        string? metadataTempPath = null;

        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using var zip = ZipFile.Create(zipPath);
            zip.BeginUpdate();

            metadataTempPath = AddBuildMetadata(zip, build, mode);

            foreach (var folder in GetExportFolders(mode))
            {
                var src = Path.Combine(gameDir, folder);
                if (!Directory.Exists(src))
                    continue;

                AddDirectory(zip, src, folder);
            }

            if (mode == BuildExportMode.FullBackup)
            {
                foreach (var fileName in RootFiles)
                {
                    var src = Path.Combine(gameDir, fileName);
                    if (File.Exists(src))
                        zip.Add(src, fileName);
                }
            }

            zip.CommitUpdate();
        }
        finally
        {
            if (!string.IsNullOrEmpty(metadataTempPath) && File.Exists(metadataTempPath))
            {
                try { File.Delete(metadataTempPath); } catch { }
            }
        }
    }

    public static void ImportInto(string zipPath, BuildInfo build) =>
        ImportExtractedContent(ExtractZip(zipPath), build);

    public static BuildInfo Import(string zipPath, string instancesRoot)
    {
        var tempDir = ExtractZip(zipPath);

        try
        {
            var metaPath = Path.Combine(tempDir, "build-export.json");
            BuildInfo build;

            if (File.Exists(metaPath))
            {
                var json = File.ReadAllText(metaPath);
                build = JsonSerializer.Deserialize<BuildInfo>(json) ?? new BuildInfo();
                build.Id = Guid.NewGuid().ToString();
                build.InstancePath = Path.Combine(instancesRoot, build.Id);
            }
            else
            {
                build = new BuildInfo
                {
                    Name = Path.GetFileNameWithoutExtension(zipPath),
                    InstancePath = Path.Combine(instancesRoot, Guid.NewGuid().ToString())
                };
                build.Id = Path.GetFileName(build.InstancePath);
            }

            ImportExtractedContent(tempDir, build);
            return build;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static string ExtractZip(string zipPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "apeiron-import-" + Guid.NewGuid().ToString("N"));
        ZipExtractHelper.ExtractZipFile(zipPath, tempDir);
        return tempDir;
    }

    private static void ImportExtractedContent(string tempDir, BuildInfo build)
    {
        build.EnsureInstanceFolders();
        var gameDir = build.GetGameDir();

        foreach (var folder in GetAllImportFolders())
        {
            var src = Path.Combine(tempDir, folder);
            if (!Directory.Exists(src))
                continue;

            CopyDirectory(src, Path.Combine(gameDir, folder));
        }

        foreach (var fileName in RootFiles)
        {
            var src = Path.Combine(tempDir, fileName);
            if (!File.Exists(src))
                continue;

            File.Copy(src, Path.Combine(gameDir, fileName), overwrite: true);
        }

        var overridesDir = Path.Combine(tempDir, "overrides");
        if (Directory.Exists(overridesDir))
            CopyDirectory(overridesDir, gameDir);
    }

    private static string[] GetExportFolders(BuildExportMode mode) =>
        mode == BuildExportMode.FullBackup
            ? [..ModpackFolders, ..FullBackupExtraFolders]
            : ModpackFolders;

    private static string[] GetAllImportFolders() =>
        [..ModpackFolders, ..FullBackupExtraFolders];

    private static string AddBuildMetadata(ZipFile zip, BuildInfo build, BuildExportMode mode)
    {
        var payload = new
        {
            build.Id,
            build.Name,
            build.MinecraftVersion,
            build.Loader,
            build.LoaderVersion,
            build.InstancePath,
            build.InstallFabricApi,
            build.IsModded,
            build.ModsEnabled,
            build.JvmArgs,
            build.RamGb,
            build.ResolutionWidth,
            build.ResolutionHeight,
            build.Fullscreen,
            ExportMode = mode.ToString()
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var temp = Path.Combine(Path.GetTempPath(), "build-export-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(temp, json);
        zip.Add(temp, "build-export.json");
        return temp;
    }

    private static void AddDirectory(ZipFile zip, string sourceDir, string entryPrefix)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            zip.Add(file, $"{entryPrefix}/{relative}");
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
