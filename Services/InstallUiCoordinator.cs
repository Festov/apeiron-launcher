using System;
using System.Threading;

namespace Apeiron.Services;

public readonly record struct ProgressUpdate(bool UpdateBarValue, int BarValue, string StatusText);

public static class DownloadProgressHelper
{
    public static ProgressUpdate CreateUpdate(int progress, string text) =>
        new(
            progress >= 0,
            progress >= 0 ? Math.Clamp(progress, 0, 100) : 0,
            text ?? string.Empty);
}

public sealed class InstallUiCoordinator : IDisposable
{
    private readonly InstallSession _session = new();
    private string? _lastInstallLogPath;

    public bool IsActive => _session.IsActive;
    public string? LastInstallLogPath => _lastInstallLogPath;

    public CancellationToken BeginDownload()
    {
        _lastInstallLogPath = null;
        return _session.Begin();
    }

    public void EndDownload() => _session.End();

    public void CancelDownload() => _session.Cancel();

    public void RecordLogLine(string line)
    {
        if (_session.IsActive)
            _session.RecordLogLine(line);
    }

    public string? SaveInstallFailureLog(string buildName, LogService logService, bool wasCancelled)
    {
        if (wasCancelled)
            return null;

        var logPath = _session.SaveFailureLog(buildName, logService);
        if (string.IsNullOrEmpty(logPath))
            return null;

        _lastInstallLogPath = logPath;
        return logPath;
    }

    public void Dispose() => _session.Dispose();
}
