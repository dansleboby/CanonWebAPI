using Canon.Core;
using Serilog;
using Serilog.Core;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

using var canonCamera = new CanonCamera(Log.Logger);
Console.WriteLine(await canonCamera.GetCameraName());

Console.WriteLine();
Console.WriteLine(await canonCamera.GetValue(CameraProperty.ISOSpeed));
Console.WriteLine(string.Join(" | ", await canonCamera.GetSupportedValues(CameraProperty.ISOSpeed)));

Console.WriteLine();
Console.WriteLine(await canonCamera.GetValue(CameraProperty.Aperture));
Console.WriteLine(string.Join(" | ", await canonCamera.GetSupportedValues(CameraProperty.Aperture)));

Console.WriteLine();
Console.WriteLine(await canonCamera.GetValue(CameraProperty.ShutterSpeed));
Console.WriteLine(string.Join(" | ", await canonCamera.GetSupportedValues(CameraProperty.ShutterSpeed)));

Console.WriteLine();
Console.WriteLine(await canonCamera.GetValue(CameraProperty.WhiteBalance));
Console.WriteLine(string.Join(" | ", await canonCamera.GetSupportedValues(CameraProperty.WhiteBalance)));

Console.WriteLine();
var bytes = await canonCamera.TakePicture();
Console.WriteLine($"Picture taken, {bytes.Length} bytes received.");

Console.ReadKey();