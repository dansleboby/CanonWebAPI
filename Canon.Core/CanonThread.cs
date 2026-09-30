using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Canon.Core;

/// <summary>
/// A dedicated STA thread that runs every Canon SDK call.
/// EDSDK callbacks are raised on the thread that opened the session, while <see cref="EDSDK.EdsGetEvent"/> is pumped,
/// so all SDK calls and event dispatching happen here.
/// </summary>
internal sealed class CanonThread : IDisposable
{
    private interface ITaskDesc
    {
        void Run();
        void Cancel(Exception exception);
    }

    private sealed class TaskDesc<T>(Func<T> func) : ITaskDesc
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _completion.Task;

        public void Run()
        {
            try
            {
                _completion.TrySetResult(func());
            }
            catch (Exception e)
            {
                _completion.TrySetException(e);
            }
        }

        public void Cancel(Exception exception) => _completion.TrySetException(exception);
    }

    /// <summary>
    /// Maximum time the thread waits for work before pumping SDK events again.
    /// </summary>
    private static readonly TimeSpan EventPumpInterval = TimeSpan.FromMilliseconds(10);

    private readonly Thread _thread;
    private readonly BlockingCollection<ITaskDesc> _queue = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action _pumpEvents;
    private readonly ILogger? _logger;
    private int _disposed;
    private DateTime _lastPumpErrorLog = DateTime.MinValue;

    /// <param name="logger">Optional logger.</param>
    /// <param name="pumpEvents">Called regularly to dispatch SDK events. Defaults to <see cref="EDSDK.EdsGetEvent"/>.</param>
    public CanonThread(ILogger? logger = null, Action? pumpEvents = null)
    {
        _logger = logger;
        _pumpEvents = pumpEvents ?? (() => EDSDK.EdsGetEvent());

        _thread = new Thread(Loop) { Name = "Canon thread", IsBackground = true };
        // EDSDK requires COM to be initialized as single-threaded apartment on the thread that talks to the camera.
        if (OperatingSystem.IsWindows())
            _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _logger?.LogInformation("Canon thread started");
    }

    /// <summary>
    /// True when the caller is running on the Canon thread (e.g. inside an EDSDK callback).
    /// </summary>
    public bool IsCurrentThread => Thread.CurrentThread == _thread;

    private void Loop()
    {
        var token = _cancellation.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryTake(out var item, (int)EventPumpInterval.TotalMilliseconds, token))
                    item.Run();

                PumpEvents();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Unexpected error on the Canon thread");
            }
        }

        // Fail whatever is still queued so no caller waits forever.
        while (_queue.TryTake(out var pending))
            pending.Cancel(new ObjectDisposedException(nameof(CanonThread), "Canon thread is stopped"));
    }

    private void PumpEvents()
    {
        try
        {
            _pumpEvents();
        }
        catch (Exception e)
        {
            // Called every few milliseconds: log a persistent failure once a minute only.
            if (DateTime.UtcNow - _lastPumpErrorLog > TimeSpan.FromMinutes(1))
            {
                _lastPumpErrorLog = DateTime.UtcNow;
                _logger?.LogError(e, "Error while pumping EDSDK events");
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="taskFunc"/> on the Canon thread.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<T> taskFunc)
    {
        ArgumentNullException.ThrowIfNull(taskFunc);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var item = new TaskDesc<T>(taskFunc);

        // Running inline avoids a deadlock when called from the Canon thread itself and awaited synchronously.
        if (IsCurrentThread)
        {
            item.Run();
            return item.Task;
        }

        try
        {
            _queue.Add(item);
        }
        catch (InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(CanonThread), "Canon thread is stopped");
        }

        return item.Task;
    }

    public Task InvokeAsync(Action taskFunc) => InvokeAsync(() =>
    {
        taskFunc();
        return true;
    });

    /// <summary>
    /// Queues <paramref name="action"/> to run later on the Canon thread, even when called from the Canon thread.
    /// Use it from EDSDK callbacks to run SDK calls once the callback has returned.
    /// </summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var item = new TaskDesc<bool>(() =>
        {
            action();
            return true;
        });

        if (!_queue.IsAddingCompleted)
        {
            try
            {
                _queue.Add(item);
            }
            catch (InvalidOperationException)
            {
                // Stopping: the action is dropped.
            }
        }

        item.Task.ContinueWith(t => _logger?.LogError(t.Exception, "Error in posted Canon action"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue.CompleteAdding();
        _cancellation.Cancel();

        // A camera that stopped answering can block an SDK call: do not block the caller forever
        // (the thread is a background thread and will not keep the process alive).
        if (!IsCurrentThread && !_thread.Join(TimeSpan.FromSeconds(10)))
            _logger?.LogWarning("The Canon thread did not stop in time");

        _logger?.LogInformation("Canon thread stopped");
    }
}
