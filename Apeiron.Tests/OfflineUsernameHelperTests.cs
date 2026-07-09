using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class OfflineUsernameHelperTests
{
    [Theory]
    [InlineData("Player", true)]
    [InlineData("Test_User01", true)]
    [InlineData("ab", false)]
    [InlineData("Иван", false)]
    [InlineData("name!", false)]
    [InlineData("", false)]
    public void IsValid_respects_minecraft_rules(string name, bool expected) =>
        Assert.Equal(expected, OfflineUsernameHelper.IsValid(name));

    [Fact]
    public void Sanitize_returns_default_for_invalid_name() =>
        Assert.Equal(OfflineUsernameHelper.Default, OfflineUsernameHelper.Sanitize("Иван"));

    [Fact]
    public void NormalizeInput_strips_invalid_characters() =>
        Assert.Equal("_01", OfflineUsernameHelper.NormalizeInput("Иван_01!"));

    [Theory]
    [InlineData("Player", OfflineUsernameValidation.Valid)]
    [InlineData("", OfflineUsernameValidation.Empty)]
    [InlineData("ab", OfflineUsernameValidation.TooShort)]
    [InlineData("Иван", OfflineUsernameValidation.Empty)]
    public void Validate_reports_specific_errors(string name, OfflineUsernameValidation expected) =>
        Assert.Equal(expected, OfflineUsernameHelper.Validate(name));
}
