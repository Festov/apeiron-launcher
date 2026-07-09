using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class LogServiceTests
{
    [Fact]
    public void SaveInstallLog_writes_file_with_lines()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var service = new LogService(root);
            var path = service.SaveInstallLog("Test Build", new[] { "line-1", "line-2" });

            Assert.False(string.IsNullOrEmpty(path));
            Assert.True(File.Exists(path));
            var content = File.ReadAllText(path);
            Assert.Contains("line-1", content);
            Assert.Contains("line-2", content);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
