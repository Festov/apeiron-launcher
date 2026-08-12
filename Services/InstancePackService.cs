using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Apeiron.Services;

public sealed class InstancePackEntry
{
    public string FileName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsFolder { get; init; }
    public string ListLabel => FileName;
}

public static class InstancePackService
{
    public static IReadOnlyList<InstancePackEntry> ListPacks(string gameDir, string folderName)
    {
        var dir = Path.Combine(gameDir, folderName);
        Directory.CreateDirectory(dir);
        var list = new List<InstancePackEntry>();

        foreach (var path in Directory.EnumerateFileSystemEntries(dir))
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.'))
                continue;

            var isDir = Directory.Exists(path);
            var isZip = !isDir && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (!isDir && !isZip)
                continue;

            list.Add(new InstancePackEntry
            {
                FileName = name,
                FullPath = path,
                IsFolder = isDir
            });
        }

        return list.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void OpenFolder(string gameDir, string folderName)
    {
        var dir = Path.Combine(gameDir, folderName);
        Directory.CreateDirectory(dir);
        LogService.OpenLogsFolder(dir);
    }

    public static int ImportFiles(string gameDir, string folderName, IEnumerable<string> sourcePaths)
    {
        var dir = Path.Combine(gameDir, folderName);
        Directory.CreateDirectory(dir);
        var count = 0;
        foreach (var source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                continue;
            if (!source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;
            var dest = Path.Combine(dir, Path.GetFileName(source));
            File.Copy(source, dest, overwrite: true);
            count++;
        }
        return count;
    }
}
