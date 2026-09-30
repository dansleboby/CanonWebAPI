using Canon.Core;

namespace Canon.Core.Tests;

public class CanonThreadTests
{
    [Fact]
    public async Task Tasks_run_on_the_dedicated_thread()
    {
        using var thread = new CanonThread(pumpEvents: () => { });

        var threadName = await thread.InvokeAsync(() => Thread.CurrentThread.Name);

        Assert.Equal("Canon thread", threadName);
    }

    [Fact]
    public async Task Exceptions_are_returned_to_the_caller()
    {
        using var thread = new CanonThread(pumpEvents: () => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => thread.InvokeAsync(() => throw new InvalidOperationException()));
    }

    [Fact]
    public async Task Events_are_pumped_while_idle()
    {
        var pumped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var thread = new CanonThread(pumpEvents: () => pumped.TrySetResult());

        await pumped.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Event_pump_errors_do_not_stop_the_thread()
    {
        using var thread = new CanonThread(pumpEvents: () => throw new DllNotFoundException("EDSDK.dll"));

        await Task.Delay(50);

        Assert.Equal(2, await thread.InvokeAsync(() => 2));
    }

    [Fact]
    public async Task Posted_actions_run_after_the_current_one()
    {
        using var thread = new CanonThread(pumpEvents: () => { });
        var order = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await thread.InvokeAsync(() =>
        {
            thread.Post(() =>
            {
                order.Add("posted");
                done.SetResult();
            });
            order.Add("current");
        });

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["current", "posted"], order);
    }

    [Fact]
    public async Task Invoke_from_the_thread_runs_inline()
    {
        using var thread = new CanonThread(pumpEvents: () => { });

        var result = await thread.InvokeAsync(() => thread.InvokeAsync(() => 42).Result);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Dispose_fails_pending_tasks_instead_of_hanging()
    {
        var thread = new CanonThread(pumpEvents: () => { });
        var blocker = new ManualResetEventSlim();

        var running = thread.InvokeAsync(() => blocker.Wait(TimeSpan.FromSeconds(5)));
        var pending = thread.InvokeAsync(() => 1);

        var dispose = Task.Run(thread.Dispose);
        await Task.Delay(100);
        blocker.Set();

        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        // InvokeAsync throws synchronously once the thread is disposed.
        Exception? error = null;
        try
        {
            _ = thread.InvokeAsync(() => 1);
        }
        catch (ObjectDisposedException e)
        {
            error = e;
        }
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Dispose_does_not_deadlock_under_load()
    {
        for (var i = 0; i < 20; i++)
        {
            var thread = new CanonThread(pumpEvents: () => { });
            using var stop = new CancellationTokenSource();

            var producer = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        await thread.InvokeAsync(() => 1);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            });

            await Task.Delay(10);
            await Task.Run(thread.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel();
            await producer.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
