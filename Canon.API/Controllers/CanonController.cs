using Canon.Core;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Canon.API.Controllers;

[ApiController]
[Route("[controller]")]
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
        return Ok(await _camera.GetCameraName());
    }

    [HttpGet("iso")]
    public async Task<IActionResult> GetIso()
    {
        return Ok(new { value = await _camera.GetValue(CameraProperty.ISOSpeed), supportedValues = await _camera.GetSupportedValues(CameraProperty.ISOSpeed) });
    }

    [HttpPost("iso")]
    public async Task<IActionResult> SetIso([Required][FromBody] string value)
    {
        await _camera.SetValue(CameraProperty.ISOSpeed, value);
        return Ok();
    }

    [HttpGet("aperture")]
    public async Task<IActionResult> GetAperture()
    {
        return Ok(new { value = await _camera.GetValue(CameraProperty.Aperture), supportedValues = await _camera.GetSupportedValues(CameraProperty.Aperture) });
    }

    [HttpPost("aperture")]
    public async Task<IActionResult> SetAperture([Required][FromBody] string value)
    {
        await _camera.SetValue(CameraProperty.Aperture, value);
        return Ok();
    }

    [HttpGet("shutterspeed")]
    public async Task<IActionResult> GetShutterSpeed()
    {
        return Ok(new { value = await _camera.GetValue(CameraProperty.ShutterSpeed), supportedValues = await _camera.GetSupportedValues(CameraProperty.ShutterSpeed) });
    }

    [HttpPost("shutterspeed")]
    public async Task<IActionResult> SetShutterSpeed([Required][FromBody] string value)
    {
        await _camera.SetValue(CameraProperty.ShutterSpeed, value);
        return Ok();
    }
        
    [HttpGet("exposure")]
    public async Task<IActionResult> GetExposureCompensation()
    {
        return Ok(new { value = await _camera.GetValue(CameraProperty.ExposureCompensation), supportedValues = await _camera.GetSupportedValues(CameraProperty.ExposureCompensation) });
    }
        
    [HttpPost("exposure")]
    public async Task<IActionResult> SetExposureCompensation([Required][FromBody] string value)
    {
        await _camera.SetValue(CameraProperty.ExposureCompensation, value);
        return Ok();
    }
        
    [HttpGet("whitebalance")]
    public async Task<IActionResult> GetWhiteBalance()
    {
        return Ok(new { value = await _camera.GetValue(CameraProperty.WhiteBalance), supportedValues = await _camera.GetSupportedValues(CameraProperty.WhiteBalance) });
    }
        
    [HttpPost("whitebalance")]
    public async Task<IActionResult> SetWhiteBalance([Required][FromBody] string value)
    {
        await _camera.SetValue(CameraProperty.WhiteBalance, value);
        return Ok();
    }

    [HttpPost("takepicture")]
    public async Task<IActionResult> TakePicture(bool useAutoFocus = true)
    {
        return File(await _camera.TakePicture(useAutoFocus), "image/jpeg");
    }

    [HttpGet("videostream")]
    public async Task GetVideoStream()
    {
        Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
        var ct = HttpContext.RequestAborted;

        try
        {
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
            // Normal client disconnect
        }
    }
}