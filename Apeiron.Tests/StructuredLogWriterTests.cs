using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class StructuredLogWriterTests
{
    [Fact]
    public void Append_writes_json_line_with_event_and_message()
    {
        var logsDir = Path.Combine(Path.GetTempPath(), "apeiron-jsonl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logsDir);

        try
        {
            StructuredLogWriter.Append(logsDir, "install.failed", "download error");

            var path = Path.Combine(logsDir, StructuredLogWriter.FileName);
            Assert.True(File.Exists(path));

            var line = File.ReadAllLines(path).Single();
            Assert.Contains("\"event\":\"install.failed\"", line.Replace(" ", ""));
            Assert.Contains("download error", line);
        }
        finally
        {
            try { Directory.Delete(logsDir, true); } catch { }
        }
    }
}
