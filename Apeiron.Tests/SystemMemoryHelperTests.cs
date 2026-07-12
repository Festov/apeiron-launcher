using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class SystemMemoryHelperTests
{
    [Fact]
    public void GetRecommendedMaxRamGb_returns_value_in_valid_range()
    {
        var maxRam = SystemMemoryHelper.GetRecommendedMaxRamGb();

        Assert.InRange(maxRam, 1, 64);
    }
}
