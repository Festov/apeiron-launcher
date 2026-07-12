using System.Net;
using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class DownloadResumeHelperTests
{
    [Fact]
    public void GetExistingBytes_returns_zero_for_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".part");
        Assert.Equal(0, DownloadResumeHelper.GetExistingBytes(path));
    }

    [Fact]
    public void ParseContentRangeTotal_reads_total_size()
    {
        Assert.Equal(5000, DownloadResumeHelper.ParseContentRangeTotal("bytes 1000-1999/5000"));
        Assert.Null(DownloadResumeHelper.ParseContentRangeTotal("bytes 0-0/*"));
    }

    [Theory]
    [InlineData(HttpStatusCode.PartialContent, 1024, true)]
    [InlineData(HttpStatusCode.OK, 1024, false)]
    [InlineData(HttpStatusCode.PartialContent, 0, false)]
    public void ShouldAppendToExistingFile_checks_status_and_existing_bytes(HttpStatusCode code, long existing, bool expected) =>
        Assert.Equal(expected, DownloadResumeHelper.ShouldAppendToExistingFile(code, existing));

    [Theory]
    [InlineData(HttpStatusCode.OK, 1024, true)]
    [InlineData(HttpStatusCode.PartialContent, 1024, false)]
    public void ShouldRestartDownload_detects_servers_without_range_support(HttpStatusCode code, long existing, bool expected) =>
        Assert.Equal(expected, DownloadResumeHelper.ShouldRestartDownload(code, existing));
}
