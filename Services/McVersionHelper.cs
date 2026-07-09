using System;

namespace Apeiron.Services;

public static class McVersionHelper
{
    public static bool IsExperimental(string id, string type)
    {
        if (type is "snapshot" or "old_beta" or "old_alpha")
            return true;

        if (type != "release")
            return true;

        var lower = id.ToLowerInvariant();
        return lower.Contains("pre") || lower.Contains("rc") || lower.Contains("snapshot");
    }

    public static bool IsStableRelease(string id, string type) =>
        type == "release" && !IsExperimental(id, type);

    public static bool MatchesSearch(string versionId, string query) =>
        string.IsNullOrEmpty(query) ||
        versionId.Contains(query, StringComparison.OrdinalIgnoreCase);

    public static int CompareVersions(string a, string b)
    {
        try
        {
            var partsA = a.Split('.');
            var partsB = b.Split('.');

            for (int i = 0; i < Math.Min(partsA.Length, partsB.Length); i++)
            {
                if (int.TryParse(partsA[i], out int numA) && int.TryParse(partsB[i], out int numB))
                {
                    if (numA != numB) return numA.CompareTo(numB);
                }
                else
                {
                    var cmp = string.Compare(partsA[i], partsB[i], StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                }
            }

            return partsA.Length.CompareTo(partsB.Length);
        }
        catch
        {
            return string.Compare(a, b, StringComparison.Ordinal);
        }
    }
}
