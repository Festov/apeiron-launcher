using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class ModListFilterTests
{
    [Fact]
    public void Filter_matches_file_name_display_name_and_version()
    {
        var mods = new List<ModManager.ModEntry>
        {
            new() { FileName = "sodium.jar", DisplayName = "Sodium", ModVersion = "0.5.0", IsEnabled = true },
            new() { FileName = "lithium.jar", DisplayName = "Lithium", ModVersion = "0.11.0", IsEnabled = true }
        };

        Assert.Equal(2, ModListFilter.Filter(mods, null).Count);
        Assert.Single(ModListFilter.Filter(mods, "sod"));
        Assert.Single(ModListFilter.Filter(mods, "0.11"));
        Assert.Empty(ModListFilter.Filter(mods, "forge"));
    }
}
