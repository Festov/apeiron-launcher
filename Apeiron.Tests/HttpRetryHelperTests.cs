using System.Net;
using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class HttpRetryHelperTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void IsTransientStatusCode_classifies_server_errors(HttpStatusCode code, bool expected) =>
        Assert.Equal(expected, HttpRetryHelper.IsTransientStatusCode(code));

    [Fact]
    public void GetBackoff_increases_with_attempt() =>
        Assert.True(HttpRetryHelper.GetBackoff(2) > HttpRetryHelper.GetBackoff(1));
}
