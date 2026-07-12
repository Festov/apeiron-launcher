using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class BuildListHelperTests
{
    [Fact]
    public void FindByIdOrName_matches_id_name_and_display_name()
    {
        var builds = new List<BuildInfo>
        {
            new() { Id = "id-1", Name = "Custom", MinecraftVersion = "1.20.1" },
            new() { Id = "id-2", Name = "", MinecraftVersion = "1.21", Loader = "Fabric", LoaderVersion = "0.15.0", IsModded = true }
        };

        Assert.Equal("id-1", BuildListHelper.FindByIdOrName(builds, "id-1")?.Id);
        Assert.Equal("id-1", BuildListHelper.FindByIdOrName(builds, "custom")?.Id);
        Assert.Equal("id-2", BuildListHelper.FindByIdOrName(builds, builds[1].DisplayName)?.Id);
        Assert.Null(BuildListHelper.FindByIdOrName(builds, "missing"));
    }
}
