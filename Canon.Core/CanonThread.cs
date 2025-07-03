using Microsoft.Extensions.Logging;

namespace Canon.Core;

/// <summary>
/// A thread that runs in the background to handle Canon SDK tasks. 
/// </summary>
internal class CanonThread: IDisposable
{
    private interface ITaskDesc
    {
        void Run();
    }

    private class TaskDesc<T> : ITaskDesc
    {
        private readonly TaskCompletionSource<T> _completion = new();
        private readonly Func<T> _func;

        public TaskDesc(Func<T> func)
        {
            _func = func ?? throw new ArgumentNullException(nameof(func));
        }

        public Task<T> Task => _completion.Task;

        public void Run()
        {
            try
            {
                var result = _func();
                _completion.SetResult(result);
            }
            catch (Exception e)
            {
                _completion.SetException(e);
            }
        }
    }

    private readonly Thread _thread;
    private readonly CancellationTokenSource _cancellation;
    private readonly Queue<ITaskDesc> _queue = new();
    private bool _isDisposed;
    private readonly ILogger? _logger;

    public CanonThread(ILogger? logger = null)
    {
        _logger = logger;

        _cancellation = new CancellationTokenSource();
        _thread = new Thread(Loop) { Name = "Canon thread", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _logger?.LogInformation("Canon thread started");
    }

    private void Loop()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            ITaskDesc? item = null;

            lock (this)
            {    
                if (_queue.Any())
                    item = _queue.Dequeue();
            }

            if (item == null)
            {
                Thread.Sleep(1);
                EDSDK.EdsGetEvent();
            }
            else
                item.Run();
        }
    }

    public void Dispose()
    {
        lock (this)
        {
            _isDisposed = true;
            _cancellation.Cancel();
            _thread.Join();
            _logger?.LogInformation("Canon thread stopped");
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> taskFunc)
    {
        lock (this)
        {
            if (_isDisposed) throw new ObjectDisposedException("Canon thread is disposed");
            var item = new TaskDesc<T>(taskFunc);
            _queue.Enqueue(item);
            return item.Task;
        }
    }

    public Task InvokeAsync(Action taskFunc) => InvokeAsync(() => 
    {
        taskFunc.Invoke();
        return 0;
    });
}