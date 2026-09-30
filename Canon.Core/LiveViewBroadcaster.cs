using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Canon.Core;

public sealed class LiveViewBroadcasterOptions
{
    public const string SectionName = "LiveView";

    /// <summary>
    /// Delay between two live view frame downloads, in milliseconds.
    /// </summary>
    public int FrameIntervalMilliseconds { get; set; } = 30;

    /// <summary>
    /// How long the live view keeps running after the last client disconnected, in milliseconds.
    /// Avoids stopping and restarting the camera live view when a client reconnects right away.
    /// </summary>
    public int IdleStopDelayMilliseconds { get; set; } = 3000;

    /// <summary>
    /// Delay before retrying after an error (e.g. camera disconnected), in milliseconds.
    /// </summary>
    public int ErrorRetryDelayMilliseconds { get; set; } = 1000;
}

/// <summary>
/// Downloads live view frames from the camera once and shares them with every connected client.
/// The live view is started with the first client and stopped once the last one has left.
/// </summary>
public sealed class LiveViewBroadcaster : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<byte[]?>> _frameSource;
    private readonly Func<Task> _start;
    private readonly Func<Task> _stop;
    private readonly ILogger? _logger;
    private readonly LiveViewBroadcasterOptions _options;

    private readonly Lock _lock = new();
    private readonly List<Channel<byte[]>> _subscribers = [];
    private readonly CancellationTokenSource _disposed = new();
    private Task? _producer;
    private volatile byte[]? _latestFrame;

    /// <param name="frameSource">Downloads one frame; returns null when no frame is available.</param>
    /// <param name="start">Starts the live view on the camera.</param>
    /// <param name="stop">Stops the live view on the camera.</param>
    public LiveViewBroadcaster(Func<CancellationToken, Task<byte[]?>> frameSource, Func<Task> start, Func<Task> stop,
        ILogger? logger = null, LiveViewBroadcasterOptions? options = null)
    {
        _frameSource = frameSource;
        _start = start;
        _stop = stop;
        _logger = logger;
        _options = options ?? new LiveViewBroadcasterOptions();
    }

    /// <summary>
    /// Creates a broadcaster for a <see cref="CanonCamera"/>.
    /// </summary>
    public LiveViewBroadcaster(CanonCamera camera, ILogger? logger = null, LiveViewBroadcasterOptions? options = null)
        : this(_ => camera.GetLiveView(), camera.StartLiveViewAsync, camera.StopLiveViewAsync, logger, options)
    {
    }

    public int SubscriberCount
    {
        get
        {
            lock (_lock)
                return _subscribers.Count;
        }
    }

    /// <summary>
    /// True while frames are being downloaded from the camera.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _producer != null;
        }
    }

    /// <summary>
    /// The last frame downloaded, if any.
    /// </summary>
    public byte[]? LatestFrame => _latestFrame;

    /// <summary>
    /// Streams live view frames until <paramref name="cancellationToken"/> is cancelled.
    /// Slow readers skip frames instead of slowing down the other clients.
    /// </summary>
    public async IAsyncEnumerable<byte[]> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
            _subscribers.Add(channel);
            _producer ??= Task.Run(ProduceAsync);
        }

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
                yield return frame;
        }
        finally
        {
            lock (_lock)
                _subscribers.Remove(channel);
        }
    }

    private async Task ProduceAsync()
    {
        var token = _disposed.Token;
        // started: the live view was started on the camera and must be stopped when the producer exits.
        // needsStart: the live view must be (re)started before the next frame, e.g. after an error.
        var started = false;
        var needsStart = true;
        var interval = TimeSpan.FromMilliseconds(Math.Max(1, _options.FrameIntervalMilliseconds));
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(0, _options.IdleStopDelayMilliseconds));
        var errorDelay = TimeSpan.FromMilliseconds(Math.Max(1, _options.ErrorRetryDelayMilliseconds));
        DateTime? idleSince = null;

        while (true)
        {
            Channel<byte[]>[] subscribers;
            lock (_lock)
            {
                if (token.IsCancellationRequested)
                {
                    // Disposing: end every client stream.
                    foreach (var subscriber in _subscribers)
                        subscriber.Writer.TryComplete();
                    _subscribers.Clear();
                }

                subscribers = [.. _subscribers];
            }

            try
            {
                if (subscribers.Length == 0)
                {
                    idleSince ??= DateTime.UtcNow;

                    if (DateTime.UtcNow - idleSince >= idleDelay || token.IsCancellationRequested)
                    {
                        if (started)
                        {
                            started = false;
                            needsStart = true;
                            await StopSafelyAsync();
                        }

                        lock (_lock)
                        {
                            // A client may have subscribed while the live view was stopping.
                            if (_subscribers.Count == 0 || token.IsCancellationRequested)
                            {
                                _producer = null;
                                return;
                            }
                        }

                        idleSince = null;
                        continue;
                    }

                    await Task.Delay(interval, token);
                    continue;
                }

                idleSince = null;

                if (needsStart)
                {
                    started = true;
                    await _start();
                    needsStart = false;
                    _logger?.LogInformation("Live view broadcast started");
                }

                var frame = await _frameSource(token);
                if (frame is { Length: > 0 })
                {
                    _latestFrame = frame;
                    foreach (var subscriber in subscribers)
                        subscriber.Writer.TryWrite(frame);
                }

                await Task.Delay(interval, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Disposing: loop once more to stop the live view and exit.
            }
            catch (Exception e)
            {
                _logger?.LogWarning("Live view error, retrying in {Delay} ms: {Message}", errorDelay.TotalMilliseconds, e.Message);
                // Keep "started": the live view is still stopped when the last client leaves.
                needsStart = true;
                _latestFrame = null;

                try
                {
                    await Task.Delay(errorDelay, token);
                }
                catch (OperationCanceledException)
                {
                    // Disposing.
                }
            }
        }
    }

    private async Task StopSafelyAsync()
    {
        try
        {
            await _stop();
            _logger?.LogInformation("Live view broadcast stopped");
        }
        catch (Exception e)
        {
            _logger?.LogWarning("Could not stop the live view: {Message}", e.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? producer;

        lock (_lock)
        {
            if (_disposed.IsCancellationRequested)
                return;

            _disposed.Cancel();
            producer = _producer;
        }

        if (producer != null)
        {
            try
            {
                await producer.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("Timed out while stopping the live view broadcast");
            }
        }

        _disposed.Dispose();
    }
}
