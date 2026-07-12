using System;
using System.Collections.Generic;
using System.Linq;

namespace Apeiron.Services;

public static class BuildListHelper
{
    public static BuildInfo? FindByIdOrName(IEnumerable<BuildInfo> builds, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return builds.FirstOrDefault(build =>
            build.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            build.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            build.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}
