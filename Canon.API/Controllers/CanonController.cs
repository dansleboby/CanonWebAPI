using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Canon.API.Models;
using Canon.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Canon.API.Controllers;

/// <summary>
/// Camera control endpoints. Errors are returned as problem details (see <see cref="Infrastructure.CameraExceptionHandler"/>):
/// 400 invalid value, 409 capture refused by the camera, 503 camera not connected or busy, 504 capture timeout.
/// </summary>
[ApiController]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public class CanonController(ILogger<CanonController> logger, CanonCamera camera, LiveViewBroadcaster liveView) : ControllerBase
{
    private static readonly byte[] FrameBoundary = "--frame\r\nContent-Type: image/jpeg\r\nContent-Length: "u8.ToArray();
    private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] FrameEnd = "\r\n"u8.ToArray();

    [HttpGet("cameraname")]
    [EndpointSummary("Camera model name")]
    [ProducesResponseType<string>(StatusCodes.Status200OK, "text/plain")]
    public async Task<IActionResult> GetCameraName() => Ok(await camera.GetCameraName());

    [HttpGet("status")]
    [EndpointSummary("Camera connection, shooting mode, temperature and live view state")]
    [EndpointDescription("Never fails because of the camera: when it is not connected, 'connected' is false and 'error' explains why.")]
    [ProducesResponseType<CameraStatusResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            await camera.ConnectAsync();

            return Ok(new CameraStatusResponse(
                Connected: true,
                CameraName: await camera.GetCameraName(),
                LiveViewActive: camera.IsLiveViewActive,
                LiveViewClients: liveView.SubscriberCount,
                Mode: await camera.GetMode(),
                Temperature: await camera.GetTemperatureStatus(),
                Flash: await camera.GetFlashStatus(),
                Error: null));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Ok(new CameraStatusResponse(camera.IsConnected, null, camera.IsLiveViewActive, liveView.SubscriberCount, null, null, null, e.Message));
        }
    }

    [HttpGet("mode")]
    [EndpointSummary("Shooting mode of the camera")]
    [EndpointDescription("AE mode set with the mode dial (e.g. 'Manual Exposure', 'Scene Intelligent Auto'), whether exposure settings can be changed remotely (isCreativeZone) and whether the camera is in movie mode.")]
    [ProducesResponseType<CameraMode>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> GetMode() => Ok(await camera.GetMode());

    [HttpGet("temperature")]
    [EndpointSummary("Temperature restrictions of the camera")]
    [EndpointDescription("Restrictions applied by the camera because of its internal temperature. 'isSupported' is false when the camera does not report it.")]
    [ProducesResponseType<TemperatureStatus>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> GetTemperature() => Ok(await camera.GetTemperatureStatus());

    [HttpGet("flash")]
    [EndpointSummary("State of the \"flash firing\" setting")]
    [EndpointDescription("The SDK only reports values set remotely: 'firing' is the value last set by the API (null before the first one). A change made in the camera menu is not visible. With Canon:ForceFlashFiring (default), the setting is set to Fire when the camera connects and before every capture.")]
    [ProducesResponseType<FlashStatus>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> GetFlash() => Ok(await camera.GetFlashStatus());

    [HttpPost("flash")]
    [EndpointSummary("Set the \"flash firing\" setting (\"fire\" or \"off\")")]
    [EndpointDescription("The camera must be in P, Tv, Av or M. With Canon:ForceFlashFiring (default), the setting is set back to Fire before the next capture.")]
    [ProducesResponseType<FlashStatus>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> SetFlash([Required][FromBody] string value)
    {
        if (!FlashStatus.TryParseFiring(value, out var firing))
            throw new ArgumentException($"Invalid flash value '{value}'. Use \"fire\" or \"off\".", nameof(value));

        logger.LogInformation("Setting flash firing to {Firing}", firing ? "Fire" : "Off");
        await camera.SetFlashFiringAsync(firing);
        return Ok(await camera.GetFlashStatus());
    }

    private async Task<IActionResult> GetValue(CameraProperty property)
    {
        logger.LogDebug("Getting {Property} value", property);
        return Ok(new PropertyValueResponse(await camera.GetValue(property), await camera.GetSupportedValues(property)));
    }

    private async Task<IActionResult> SetValue(CameraProperty property, string value)
    {
        logger.LogInformation("Setting {Property} to {Value}", property, value);
        await camera.SetValue(property, value);
        return Ok();
    }

    [HttpGet("iso")]
    [EndpointSummary("Current ISO speed and supported values")]
    [ProducesResponseType<PropertyValueResponse>(StatusCodes.Status200OK, "application/json")]
    public Task<IActionResult> GetIso() => GetValue(CameraProperty.ISOSpeed);

    [HttpPost("iso")]
    [EndpointSummary("Set the ISO speed (e.g. \"Auto\", \"400\")")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> SetIso([Required][FromBody] string value) => SetValue(CameraProperty.ISOSpeed, value);

    [HttpGet("aperture")]
    [EndpointSummary("Current aperture and supported values")]
    [ProducesResponseType<PropertyValueResponse>(StatusCodes.Status200OK, "application/json")]
    public Task<IActionResult> GetAperture() => GetValue(CameraProperty.Aperture);

    [HttpPost("aperture")]
    [EndpointSummary("Set the aperture (e.g. \"5.6\")")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> SetAperture([Required][FromBody] string value) => SetValue(CameraProperty.Aperture, value);

    [HttpGet("shutterspeed")]
    [EndpointSummary("Current shutter speed and supported values")]
    [ProducesResponseType<PropertyValueResponse>(StatusCodes.Status200OK, "application/json")]
    public Task<IActionResult> GetShutterSpeed() => GetValue(CameraProperty.ShutterSpeed);

    [HttpPost("shutterspeed")]
    [EndpointSummary("Set the shutter speed (e.g. \"1/125\", \"2\\\"\")")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> SetShutterSpeed([Required][FromBody] string value) => SetValue(CameraProperty.ShutterSpeed, value);

    [HttpGet("exposure")]
    [EndpointSummary("Current exposure compensation and supported values")]
    [ProducesResponseType<PropertyValueResponse>(StatusCodes.Status200OK, "application/json")]
    public Task<IActionResult> GetExposureCompensation() => GetValue(CameraProperty.ExposureCompensation);

    [HttpPost("exposure")]
    [EndpointSummary("Set the exposure compensation (e.g. \"0\", \"+1/3\", \"-1 2/3\")")]
    [EndpointDescription("Not available in manual exposure mode.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> SetExposureCompensation([Required][FromBody] string value) => SetValue(CameraProperty.ExposureCompensation, value);

    [HttpGet("whitebalance")]
    [EndpointSummary("Current white balance and supported values")]
    [ProducesResponseType<PropertyValueResponse>(StatusCodes.Status200OK, "application/json")]
    public Task<IActionResult> GetWhiteBalance() => GetValue(CameraProperty.WhiteBalance);

    [HttpPost("whitebalance")]
    [EndpointSummary("Set the white balance (e.g. \"Auto\", \"Daylight\")")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> SetWhiteBalance([Required][FromBody] string value) => SetValue(CameraProperty.WhiteBalance, value);

    /// <param name="useAutoFocus">Focus before shooting (default true).</param>
    /// <param name="fileTypes">
    /// Comma separated file types to download (e.g. "jpg", "cr3", "jpg,cr3", "*").
    /// Defaults to the Canon:CaptureFileTypes setting (JPEG only). The first matching file is returned.
    /// </param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("takepicture")]
    [EndpointSummary("Take a picture and return the captured file")]
    [EndpointDescription("The timeout adapts to the shutter speed. The file name is returned in the X-File-Name header.")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "image/jpeg", "application/octet-stream")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<IActionResult> TakePicture(
        [Description("Focus before shooting.")] bool useAutoFocus = true,
        [Description("Comma separated file types to return (e.g. \"jpg\", \"cr3\", \"jpg,cr3\", \"*\"). Defaults to the Canon:CaptureFileTypes setting (JPEG only).")] string? fileTypes = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Taking picture with auto focus: {UseAutoFocus}", useAutoFocus);

        var image = await camera.TakePictureAsync(useAutoFocus, fileTypes is null ? null : [fileTypes], cancellationToken);

        logger.LogInformation("Picture taken: {File}, {Size} bytes", image.FileName, image.Data.Length);
        Response.Headers["X-File-Name"] = image.FileName;
        return File(image.Data, image.ContentType);
    }

    [HttpGet("latestpicture")]
    [EndpointSummary("Last picture received from the camera")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "image/jpeg", "application/octet-stream")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult GetLatestPicture()
    {
        var image = camera.GetLatestImage();

        if (image is null || image.Data.Length == 0)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "No picture available");

        Response.Headers["X-File-Name"] = image.FileName;
        return File(image.Data, image.ContentType);
    }

    [HttpPost("autofocus")]
    [EndpointSummary("Focus (half-press of the shutter button)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AutoFocus()
    {
        logger.LogInformation("Starting auto focus");
        await camera.AutoFocus();
        return Ok();
    }

    [HttpGet("liveview")]
    [EndpointSummary("Single live view image (JPEG)")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "image/jpeg")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetLiveViewFrame()
    {
        if (liveView.IsRunning && liveView.LatestFrame is { Length: > 0 } latest)
            return File(latest, "image/jpeg");

        await camera.ConnectAsync();

        // Goes through the broadcaster so the live view is stopped again once unused.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await foreach (var frame in liveView.ReadFramesAsync(timeout.Token))
                return File(frame, "image/jpeg");
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            // No frame within the timeout.
        }

        return Problem(statusCode: StatusCodes.Status404NotFound, title: "No live view image available yet");
    }

    /// <summary>
    /// MJPEG live view stream. Every client shares the same frames, downloaded once from the camera.
    /// </summary>
    [HttpGet("videostream")]
    [EndpointSummary("MJPEG live view stream")]
    [EndpointDescription("multipart/x-mixed-replace stream usable in an <img> tag. Several clients share the same frames. The camera live view stops a few seconds after the last client disconnects.")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "multipart/x-mixed-replace")]
    public async Task GetVideoStream()
    {
        var cancellationToken = HttpContext.RequestAborted;

        // Fails with a problem details response when no camera is connected, before the stream starts.
        await camera.ConnectAsync();

        logger.LogInformation("Video stream client connected ({Count} active)", liveView.SubscriberCount + 1);

        Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
        Response.Headers.CacheControl = "no-cache, no-store";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await foreach (var frame in liveView.ReadFramesAsync(cancellationToken))
            {
                await Response.Body.WriteAsync(FrameBoundary, cancellationToken);
                await Response.Body.WriteAsync(Encoding.ASCII.GetBytes(frame.Length.ToString()), cancellationToken);
                await Response.Body.WriteAsync(HeaderEnd, cancellationToken);
                await Response.Body.WriteAsync(frame, cancellationToken);
                await Response.Body.WriteAsync(FrameEnd, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal client disconnect.
        }
        finally
        {
            logger.LogInformation("Video stream client disconnected");
        }
    }
}
