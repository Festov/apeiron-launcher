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
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void IsTransientStatusCode_classifies_server_errors(HttpStatusCode code, bool expected) =>
        Assert.Equal(expected, HttpRetryHelper.IsTransientStatusCode(code));

    [Fact]
    public void GetBackoff_increases_with_attempt() =>
        Assert.True(HttpRetryHelper.GetBackoff(2) > HttpRetryHelper.GetBackoff(1));

    [Fact]
    public void IsCancellation_detects_token_and_aggregate()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.True(HttpRetryHelper.IsCancellation(new InvalidOperationException("x"), cts.Token));
        Assert.True(HttpRetryHelper.IsCancellation(new OperationCanceledException()));
        Assert.True(HttpRetryHelper.IsCancellation(
            new AggregateException(new HttpRequestException("403"), new OperationCanceledException())));
        Assert.False(HttpRetryHelper.IsCancellation(new HttpRequestException("403")));
    }
}