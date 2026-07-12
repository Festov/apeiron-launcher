using System;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;

namespace Apeiron.Services;

public static class ZipExtractHelper
{
    public static void ExtractZipFile(string zipPath, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        using var zip = new ZipFile(zipPath);
        foreach (ZipEntry entry in zip)
        {
            if (!entry.IsFile)
                continue;

            ExtractEntry(zip, entry, destinationDir);
        }
    }

    public static void ExtractEntry(ZipFile zip, ZipEntry entry, string destinationDir)
    {
        var relativePath = entry.Name.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Unsafe zip entry path: {entry.Name}");

        var dest = Path.GetFullPath(Path.Combine(destinationDir, relativePath));
        var root = Path.GetFullPath(destinationDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar);

        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Zip entry escapes destination: {entry.Name}");

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var input = zip.GetInputStream(entry);
        using var output = File.Create(dest);
        input.CopyTo(output);
    }
}
