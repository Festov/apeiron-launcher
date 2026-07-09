using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class JavaDownloadHelperTests
{
    [Fact]
    public void FindJdkRoot_finds_nested_jdk_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-jdk-" + Guid.NewGuid().ToString("N"));
        var jdkDir = Path.Combine(root, "nested", "jdk-21");
        Directory.CreateDirectory(Path.Combine(jdkDir, "bin"));
        File.WriteAllText(Path.Combine(jdkDir, "bin", "java.exe"), "");

        try
        {
            Assert.Equal(jdkDir, JavaDownloadHelper.FindJdkRoot(root));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Theory]
    [InlineData(11, 4)]
    [InlineData(17, 4)]
    [InlineData(21, 4)]
    public async Task GetInstallerDownloadUrls_includes_oracle_archive_mirrors(int javaMajor, int expectedCount)
    {
        var urls = await JavaDownloadHelper.GetInstallerDownloadUrlsAsync(javaMajor);
        Assert.Equal(expectedCount, urls.Count);
        Assert.All(urls, url => Assert.Contains("download.oracle.com", url));
        Assert.Contains(urls, url => url.Contains("/archive/", StringComparison.Ordinal) || url.Contains("/latest/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetInstallerDownloadUrls_for_java8_uses_otn_and_oracle_mirror()
    {
        var urls = await JavaDownloadHelper.GetInstallerDownloadUrlsAsync(8);

        Assert.True(urls.Count >= 10);
        Assert.Contains(urls, url => url.Contains("download.oracle.com/otn-pub/java/jdk/", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("jdk-8u491-windows-x64.exe", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("cfdownload.adobe.com", StringComparison.Ordinal));
    }
}
