using System;
using System.Threading;

namespace Apeiron.Services;

/// <summary>
/// Ensures only one launcher UI process runs per user session.
/// A second start signals the first instance to bring its window forward.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\Apeiron.Launcher.SingleInstance.v1";
    private const string ActivateEventName = @"Local\Apeiron.Launcher.Activate.v1";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly CancellationTokenSource? _cts;
    private readonly Thread? _waiter;
    private bool _disposed;

    public bool IsPrimary { get; }

    public event Action? Activated;

    public SingleInstanceGuard()
    {
        _mutex = CreateMutex(out var createdNew);
        IsPrimary = createdNew;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        if (!IsPrimary)
            return;

        _cts = new CancellationTokenSource();
        _waiter = new Thread(WaitForActivateSignals)
        {
            IsBackground = true,
            Name = "Apeiron.SingleInstance"
        };
        _waiter.Start();
    }

    public void RequestActivate()
    {
        try
        {
            _activateEvent.Set();
        }
        catch
        {
            // ignore — primary may already be exiting
        }
    }

    private void WaitForActivateSignals()
    {
        var token = _cts!.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_activateEvent.WaitOne(500))
                    Activated?.Invoke();
            }
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private static Mutex CreateMutex(out bool createdNew)
    {
        try
        {
            return new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);
        }
        catch (AbandonedMutexException ex)
        {
            createdNew = true;
            return ex.Mutex ?? new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _cts?.Cancel();
        }
        catch { }

        try
        {
            if (IsPrimary)
                _mutex.ReleaseMutex();
        }
        catch { }

        _mutex.Dispose();
        _activateEvent.Dispose();
        _cts?.Dispose();
    }
}
