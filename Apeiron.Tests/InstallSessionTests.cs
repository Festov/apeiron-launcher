using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class InstallSessionTests
{
    [Fact]
    public void Begin_captures_log_lines_until_end()
    {
        using var session = new InstallSession();

        var token = session.Begin();
        session.RecordLogLine("line 1");
        session.RecordLogLine("line 2");
        session.End();
        session.RecordLogLine("ignored");

        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_requests_cancellation()
    {
        using var session = new InstallSession();
        var token = session.Begin();

        session.Cancel();

        Assert.True(token.IsCancellationRequested);
        session.End();
    }

    [Fact]
    public void SaveFailureLog_writes_install_log_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "apeiron-install-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var session = new InstallSession();
            session.Begin();
            session.RecordLogLine("download failed");

            var logService = new LogService(root);
            var path = session.SaveFailureLog("Test Build", logService);

            Assert.False(string.IsNullOrEmpty(path));
            Assert.True(File.Exists(path));
            Assert.Contains("download failed", File.ReadAllText(path));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
