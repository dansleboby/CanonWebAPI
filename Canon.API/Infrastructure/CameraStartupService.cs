using Canon.Core;

namespace Canon.API.Infrastructure;

/// <summary>
/// Connects to the camera when the application starts, so the SDK is ready and the camera is detected
/// when it is plugged in later (hot plug).
/// </summary>
public sealed class CameraStartupService(CanonCamera camera, ILogger<CameraStartupService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        camera.ConnectionChanged += connected =>
            logger.LogInformation(connected ? "Camera connected" : "Camera disconnected");

        _ = Task.Run(async () =>
        {
            try
            {
                await camera.ConnectAsync();
                logger.LogInformation("Camera ready: {Name}", await camera.GetCameraName());
            }
            catch (Exception e)
            {
                logger.LogWarning("No camera at startup ({Message}); it will be connected when plugged in or on the next request", e.Message);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
