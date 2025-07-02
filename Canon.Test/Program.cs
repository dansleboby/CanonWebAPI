using Canon.Core;
using Microsoft.Extensions.Logging;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger(); 

var logger = LoggerFactory.Create(loggingBuilder => loggingBuilder.AddSerilog()).CreateLogger("");

using var canonCamera = new CanonCamera(logger);
logger.LogInformation("Camera name: {v}", await canonCamera.GetCameraName());

Console.WriteLine();
logger.LogInformation("ISO: {v}", await canonCamera.GetValue(CameraProperty.ISOSpeed));
logger.LogInformation("Supported ISO values: {v}", string.Join("|", await canonCamera.GetSupportedValues(CameraProperty.ISOSpeed)));

Console.WriteLine();
logger.LogInformation("Aperture: {v}", await canonCamera.GetValue(CameraProperty.Aperture));
logger.LogInformation("Supported Aperture values: {v}", string.Join("|", await canonCamera.GetSupportedValues(CameraProperty.Aperture)));

Console.WriteLine();
logger.LogInformation("Shutter Speed: {v}", await canonCamera.GetValue(CameraProperty.ShutterSpeed));
logger.LogInformation("Supported Shutter Speed values: {v}", string.Join("|", await canonCamera.GetSupportedValues(CameraProperty.ShutterSpeed)));

Console.WriteLine();
logger.LogInformation("White Balance: {v}", await canonCamera.GetValue(CameraProperty.WhiteBalance));
logger.LogInformation("Supported White Balance values: {v}", string.Join("|", await canonCamera.GetSupportedValues(CameraProperty.WhiteBalance)));

Console.WriteLine();
var bytes = await canonCamera.TakePicture();
logger.LogInformation("Picture taken, {v} bytes received", bytes.Length);