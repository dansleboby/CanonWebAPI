using Canon.Core;

namespace Canon.Core.Tests;

public class LiveViewBroadcasterTests
{
    private sealed class FakeCamera
    {
        private int _frame;
        public int FramesRequested;
        public int Starts;
        public int Stops;
        public int FailuresToSimulate;

        public Task<byte[]?> GetFrame(CancellationToken _)
        {
            Interlocked.Increment(ref FramesRequested);

            if (FailuresToSimulate > 0)
            {
                FailuresToSimulate--;
                throw new EdsException(0x80, "Camera unplugged");
            }

            return Task.FromResult<byte[]?>([(byte)Interlocked.Increment(ref _frame)]);
        }

        public Task Start()
        {
            Interlocked.Increment(ref Starts);
            return Task.CompletedTask;
        }

        public Task Stop()
        {
            Interlocked.Increment(ref Stops);
            return Task.CompletedTask;
        }
    }

    private static LiveViewBroadcaster Create(FakeCamera camera, int idleStopDelay = 50) =>
        new(camera.GetFrame, camera.Start, camera.Stop, options: new LiveViewBroadcasterOptions
        {
            FrameIntervalMilliseconds = 5,
            IdleStopDelayMilliseconds = idleStopDelay,
            ErrorRetryDelayMilliseconds = 20
        });

    private static async Task<List<byte[]>> Read(LiveViewBroadcaster broadcaster, int count, CancellationToken cancellationToken)
    {
        var frames = new List<byte[]>();
        await foreach (var frame in broadcaster.ReadFramesAsync(cancellationToken))
        {
            frames.Add(frame);
            if (frames.Count == count)
                break;
        }

        return frames;
    }

    [Fact]
    public async Task Two_clients_share_a_single_producer()
    {
        var camera = new FakeCamera();
        await using var broadcaster = Create(camera);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var first = Read(broadcaster, 20, timeout.Token);
        var second = Read(broadcaster, 20, timeout.Token);
        await Task.WhenAll(first, second);

        Assert.Equal(1, camera.Starts);
        // Each frame is downloaded once and shared: far fewer downloads than frames delivered.
        Assert.True(camera.FramesRequested < 40 + 10, $"{camera.FramesRequested} frames requested");
        Assert.NotNull(broadcaster.LatestFrame);
    }

    [Fact]
    public async Task Live_view_stops_after_the_last_client_leaves()
    {
        var camera = new FakeCamera();
        await using var broadcaster = Create(camera);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Read(broadcaster, 3, timeout.Token);

        while (broadcaster.IsRunning && !timeout.IsCancellationRequested)
            await Task.Delay(10);

        Assert.False(broadcaster.IsRunning);
        Assert.Equal(0, broadcaster.SubscriberCount);
        Assert.Equal(1, camera.Starts);
        Assert.Equal(1, camera.Stops);
    }

    [Fact]
    public async Task A_client_reconnecting_quickly_does_not_restart_the_live_view()
    {
        var camera = new FakeCamera();
        await using var broadcaster = Create(camera, idleStopDelay: 5000);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Read(broadcaster, 3, timeout.Token);
        await Read(broadcaster, 3, timeout.Token);

        Assert.Equal(1, camera.Starts);
        Assert.Equal(0, camera.Stops);
    }

    [Fact]
    public async Task Errors_are_retried_and_the_live_view_restarted()
    {
        var camera = new FakeCamera { FailuresToSimulate = 2 };
        await using var broadcaster = Create(camera);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var frames = await Read(broadcaster, 3, timeout.Token);

        Assert.Equal(3, frames.Count);
        Assert.Equal(3, camera.Starts);
    }

    [Fact]
    public async Task Dispose_ends_client_streams_and_stops_the_live_view()
    {
        var camera = new FakeCamera();
        var broadcaster = Create(camera, idleStopDelay: 5000);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var reader = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var _ in broadcaster.ReadFramesAsync(timeout.Token))
                count++;
            return count;
        });

        while (broadcaster.LatestFrame is null)
            await Task.Delay(5, timeout.Token);

        await broadcaster.DisposeAsync();
        var received = await reader.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(received > 0);
        Assert.Equal(1, camera.Stops);
    }
}
