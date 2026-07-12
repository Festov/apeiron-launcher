using System;
using System.Collections.Generic;
using System.Linq;

namespace Apeiron.Services;

public static class ModListFilter
{
    public static List<ModManager.ModEntry> Filter(IEnumerable<ModManager.ModEntry> mods, string? query)
    {
        var source = mods as IList<ModManager.ModEntry> ?? mods.ToList();
        if (string.IsNullOrWhiteSpace(query))
            return source.ToList();

        var term = query.Trim();
        return source
            .Where(mod =>
                mod.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                mod.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                mod.ModVersion.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
