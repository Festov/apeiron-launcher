using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class InstallUiCoordinatorTests
{
    [Fact]
    public void BeginDownload_and_EndDownload_toggle_active_state()
    {
        using var coordinator = new InstallUiCoordinator();
        Assert.False(coordinator.IsActive);

        var token = coordinator.BeginDownload();
        Assert.True(coordinator.IsActive);
        Assert.False(token.IsCancellationRequested);

        coordinator.EndDownload();
        Assert.False(coordinator.IsActive);
    }

    [Fact]
    public void SaveInstallFailureLog_skips_cancelled_installs()
    {
        using var coordinator = new InstallUiCoordinator();
        var logsDir = Path.Combine(Path.GetTempPath(), "apeiron-install-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logsDir);
        var logService = new LogService(logsDir);

        try
        {
            coordinator.BeginDownload();
            coordinator.RecordLogLine("failed step");
            var logPath = coordinator.SaveInstallFailureLog("Test Build", logService, wasCancelled: true);
            Assert.Null(logPath);
        }
        finally
        {
            try { Directory.Delete(logsDir, true); } catch { }
        }
    }

    [Fact]
    public void SaveInstallFailureLog_writes_file_when_install_failed()
    {
        using var coordinator = new InstallUiCoordinator();
        var logsDir = Path.Combine(Path.GetTempPath(), "apeiron-install-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logsDir);
        var logService = new LogService(logsDir);

        try
        {
            coordinator.BeginDownload();
            coordinator.RecordLogLine("download failed");
            var logPath = coordinator.SaveInstallFailureLog("Test Build", logService, wasCancelled: false);

            Assert.False(string.IsNullOrEmpty(logPath));
            Assert.True(File.Exists(logPath));
            Assert.Equal(logPath, coordinator.LastInstallLogPath);
        }
        finally
        {
            try { Directory.Delete(logsDir, true); } catch { }
        }
    }
}
