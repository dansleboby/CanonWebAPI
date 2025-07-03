using Canon.Core;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Canon.API.Controllers;

[ApiController]
public class CanonController : ControllerBase
{
    private readonly CanonCamera _camera;
    private readonly ILogger<CanonController> _logger;

    public CanonController(ILogger<CanonController> logger, CanonCamera camera)
    {
        _logger = logger;
        _camera = camera;
    }

    [HttpGet("cameraname")]
    public async Task<IActionResult> GetCameraName()
    {
        _logger.LogInformation("Getting camera name");
        return Ok(await _camera.GetCameraName());
    }

    private async Task<IActionResult> GetValue(CameraProperty property)
    {
        _logger.LogInformation("Getting {Property} value", property);

        try
        {
            return Ok(new { value = await _camera.GetValue(property), supportedValues = await _camera.GetSupportedValues(property) });
        }
        catch(ArgumentOutOfRangeException)
        {
            _logger.LogWarning("Invalid value for {Property}", property);
            return BadRequest($"Invalid value for {property}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting value for {Property}", property);
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task<IActionResult> SetValue(CameraProperty property, string value)
    {
        _logger.LogInformation("Setting {Property} to {Value}", property, value);

        try
        {
            await _camera.SetValue(property, value);
            return Ok();
        }
        catch (ArgumentOutOfRangeException)
        {
            _logger.LogWarning("Invalid value for {Property}", property);
            return BadRequest($"Invalid value for {property}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting value for {Property}", property);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("iso")]
    public Task<IActionResult> GetIso() => GetValue(CameraProperty.ISOSpeed);

    [HttpPost("iso")]
    public Task<IActionResult> SetIso([Required] [FromBody] string value) => SetValue(CameraProperty.ISOSpeed, value);

    [HttpGet("aperture")]
    public Task<IActionResult> GetAperture() => GetValue(CameraProperty.Aperture);

    [HttpPost("aperture")]
    public Task<IActionResult> SetAperture([Required][FromBody] string value) => SetValue(CameraProperty.Aperture, value);

    [HttpGet("shutterspeed")]
    public Task<IActionResult> GetShutterSpeed() => GetValue(CameraProperty.ShutterSpeed);

    [HttpPost("shutterspeed")]
    public Task<IActionResult> SetShutterSpeed([Required][FromBody] string value) => SetValue(CameraProperty.ShutterSpeed, value);

    [HttpGet("exposure")]
    public Task<IActionResult> GetExposureCompensation() => GetValue(CameraProperty.ExposureCompensation);

    [HttpPost("exposure")]
    public Task<IActionResult> SetExposureCompensation([Required][FromBody] string value) => SetValue(CameraProperty.ExposureCompensation, value);

    [HttpGet("whitebalance")]
    public Task<IActionResult> GetWhiteBalance() => GetValue(CameraProperty.WhiteBalance);

    [HttpPost("whitebalance")]
    public Task<IActionResult> SetWhiteBalance([Required][FromBody] string value) => SetValue(CameraProperty.WhiteBalance, value);
    
    [HttpPost("takepicture")]
    public async Task<IActionResult> TakePicture(bool useAutoFocus = true)
    {
        _logger.LogInformation("Taking picture with auto focus: {UseAutoFocus}", useAutoFocus);

        try
        {
            var bytes = await _camera.TakePicture(useAutoFocus);
            
            if (bytes?.Length > 0)
            {
                _logger.LogInformation("Picture taken successfully, size: {Size} bytes", bytes.Length);
                return File(bytes, "image/jpeg");
            }

            _logger.LogWarning("No picture data received");
            return NotFound("No picture taken");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Picture taking operation timed out");
            return StatusCode(408, "Request Timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error taking picture");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("videostream")]
    public async Task GetVideoStream()
    {
        _logger.LogInformation("Starting video stream");

        try
        {
            Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
            var ct = HttpContext.RequestAborted;

            while (!ct.IsCancellationRequested)
            {
                var bytes = await _camera.GetLiveView();

                if (bytes?.Length > 0)
                {
                    await Response.Body.WriteAsync("\r\n--frame\r\n"u8.ToArray(), ct);
                    await Response.Body.WriteAsync("Content-Type: image/jpeg\r\n"u8.ToArray(), ct);
                    await Response.Body.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"), ct);
                    await Response.Body.WriteAsync(bytes, ct);
                    await Response.Body.FlushAsync(ct);
                }

                await Task.Delay(30, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Video stream cancelled by client");
            // Normal client disconnect
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during video stream");
            Response.StatusCode = 500;
            await Response.WriteAsync("Internal server error");
        }
    }

    [HttpGet("latestpicture")]
    public async Task<IActionResult> GetLatestPicture()
    {
        _logger.LogInformation("Getting latest picture");

        try
        {
            var bytes = await _camera.GetLatestImageBytes();

            if (bytes?.Length > 0)
            {
                _logger.LogInformation("Returning latest picture with size {Size} bytes", bytes.Length);
                return File(bytes, "image/jpeg");
            }

            _logger.LogWarning("No latest picture available");
            return NotFound("No latest picture available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest picture");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("autofocus")]
    public async Task<IActionResult> AutoFocus()
    {
        _logger.LogInformation("Starting auto focus");

        try
        {
            await _camera.AutoFocus();
            _logger.LogInformation("Auto focus completed successfully");
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto focus");
            return StatusCode(500, "Internal server error");
        }
    }
}