using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Apeiron.Services;

public static class FileDropHelper
{
    public static IReadOnlyList<string> GetZipFiles(IEnumerable<string> paths) =>
        paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
