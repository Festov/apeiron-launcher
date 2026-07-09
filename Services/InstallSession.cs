using System;
using System.Collections.Generic;
using System.Threading;

namespace Apeiron.Services;

/// <summary>Tracks install cancellation token and captured console lines for failure logs.</summary>
public sealed class InstallSession : IDisposable
{
    private readonly List<string> _logLines = new();
    private CancellationTokenSource? _cts;
    private bool _capture;

    public bool IsActive => _cts != null;

    public CancellationToken Begin()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _logLines.Clear();
        _capture = true;
        return _cts.Token;
    }

    public void End()
    {
        _capture = false;
        _cts?.Dispose();
        _cts = null;
    }

    public void Cancel() => _cts?.Cancel();

    public void RecordLogLine(string line)
    {
        if (_capture)
            _logLines.Add(line);
    }

    public string? SaveFailureLog(string buildName, LogService logService)
    {
        if (_logLines.Count == 0)
            return null;

        return logService.SaveInstallLog(buildName, _logLines);
    }

    public void Dispose() => _cts?.Dispose();
}
