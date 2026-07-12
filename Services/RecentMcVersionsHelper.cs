using System;
using System.Collections.Generic;
using System.Linq;

namespace Apeiron.Services;

public static class RecentMcVersionsHelper
{
    public const int MaxCount = 8;

    public static void Record(List<string> recent, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return;

        recent.RemoveAll(v => v.Equals(version, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, version.Trim());

        if (recent.Count > MaxCount)
            recent.RemoveRange(MaxCount, recent.Count - MaxCount);
    }

    public static IReadOnlyList<string> OrderWithRecentFirst(
        IEnumerable<string> versions,
        IReadOnlyList<string> recentVersions)
    {
        var versionList = versions.ToList();
        if (recentVersions.Count == 0)
            return versionList;

        var recentOrdered = recentVersions
            .Select(recent => versionList.FirstOrDefault(v => v.Equals(recent, StringComparison.OrdinalIgnoreCase)))
            .Where(version => !string.IsNullOrEmpty(version))
            .Cast<string>()
            .ToList();

        var rest = versionList
            .Where(version => !recentVersions.Any(recent => recent.Equals(version, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return recentOrdered.Concat(rest).ToList();
    }
}
