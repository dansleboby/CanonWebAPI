using Microsoft.Extensions.Logging;

namespace Canon.Core;

public class CanonCamera : IDisposable
{
    private readonly Lock _initLock = new();
    private readonly CanonThread _thread;
    private Task? _initTask;
    private nint _camera;
    private readonly EDSDK.EdsObjectEventHandler _onCameraObject;
    private readonly EDSDK.EdsStateEventHandler _onCameraStateChanged;

    private TaskCompletionSource<byte[]>? _takePictureCompletion;

    public CanonCamera(ILogger<CanonCamera>? logger = null)
    {
        _thread = new CanonThread(logger);
        _onCameraObject = OnCameraObject;
        _onCameraStateChanged = OnCameraStateChanged;
    }

    private uint OnCameraStateChanged(uint inEvent, uint inParameter, IntPtr inContext)
    {
        if (inEvent == EDSDK.StateEvent_Shutdown)
        {
            lock (_initLock)
                _initTask = null;
        }

        return 0;
    }

    /// <summary>
    /// Initializes the Canon SDK and connects to the first detected camera.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private Task InitializeAsync()
    {
        lock (_initLock)
        {
            return _initTask ??= _thread.InvokeAsync(() =>
            {
                EDSDK.EdsInitializeSDK().ThrowIfEdSdkError("Failed to initialize EDSDK");
                EDSDK.EdsGetCameraList(out var cameraList).ThrowIfEdSdkError("Failed to get camera list");
                EDSDK.EdsGetChildCount(cameraList, out var cameraCount).ThrowIfEdSdkError("Failed to get camera count");

                try
                {
                    if (cameraCount == 0)
                        throw new InvalidOperationException("No Canon camera detected");

                    EDSDK.EdsGetChildAtIndex(cameraList, 0, out _camera).ThrowIfEdSdkError("Could not get first camera");
                }
                finally
                {
                    EDSDK.EdsRelease(cameraList);
                }

                EDSDK.EdsOpenSession(_camera).ThrowIfEdSdkError("Failed to open camera session");

                EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_SaveTo, 0, sizeof(uint), (uint)EDSDK.EdsSaveTo.Host).ThrowIfEdSdkError("Failed set SaveTo");
                EDSDK.EdsSetCapacity(_camera, new EDSDK.EdsCapacity { Reset = 1, BytesPerSector = 0x1000, NumberOfFreeClusters = 0x7FFFFFFF }).ThrowIfEdSdkError("Failed to set Capacity");
                EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), EDSDK.EvfOutputDevice_PC).ThrowIfEdSdkError("Failed to set EVF Output");
                EDSDK.EdsSetObjectEventHandler(_camera, EDSDK.ObjectEvent_All, _onCameraObject, _camera).ThrowIfEdSdkError("Failed to subscribe to ObjectEvent");
                EDSDK.EdsSetCameraStateEventHandler(_camera, EDSDK.StateEvent_All, _onCameraStateChanged, _camera).ThrowIfEdSdkError("Failed to subscribe to StateEvent");
            });
        }
    }

    /// <summary>
    /// Gets the connected camera's device name.
    /// </summary>
    public async Task<string> GetCameraName()
    {
        await InitializeAsync();
            
        if (_camera == nint.Zero) 
            throw new InvalidOperationException("Camera not initialized");

        return await _thread.InvokeAsync(() =>
        {
            EDSDK.EdsGetDeviceInfo(_camera, out var info).ThrowIfEdSdkError("Could not get device info");
            return info.szDeviceDescription;
        });
    }

    public async Task<string> GetValue(CameraProperty property)
    {
        await InitializeAsync();
            
        return await _thread.InvokeAsync(() =>
        {
            var values = ((uint)property).GetPropertyValues();

            EDSDK.EdsGetPropertyData(_camera, (uint)property, 0, out uint value).ThrowIfEdSdkError($"Could not get property 0x{property:X}");
            return values.TryGetValue(value, out var description) ? description : value.ToString();
        });
    }

    public async Task<List<string>> GetSupportedValues(CameraProperty propId)
    {
        await InitializeAsync();

        return await _thread.InvokeAsync(() =>
        {
            var values = ((uint)propId).GetPropertyValues();
            EDSDK.EdsGetPropertyDesc(_camera, (uint)propId, out var desc).ThrowIfEdSdkError("EdsGetPropertyDesc failed");
            
            return Enumerable.Range(0, desc.NumElements)
                .Select(i => values.TryGetValue((uint)desc.PropDesc[i], out var description)
                    ? description
                    : desc.PropDesc[i].ToString())
                .ToList();
        });
    }

    public async Task SetValue(CameraProperty propId, uint value)
    {
        await InitializeAsync();
        
        await _thread.InvokeAsync(() => EDSDK.EdsSetPropertyData(_camera, (uint)propId, 0, sizeof(uint), value).ThrowIfEdSdkError($"Could not set property 0x{propId:X} to {value}"));
    }

    public Task SetValue(CameraProperty propId, string description)
    {
        var descriptions = ((uint)propId).GetPropertyDescriptions();
        
        if (!descriptions.TryGetValue(description, out var value))
            throw new ArgumentException($"Invalid value '{value}' for property 0x{propId:X}");

        return SetValue(propId, value);
    }

    public async Task<byte[]> TakePicture(bool useAutoFocus = true)
    {
        await InitializeAsync();
        _takePictureCompletion = new TaskCompletionSource<byte[]>();

        await _thread.InvokeAsync(() =>
        {
            if (useAutoFocus)
            {
                EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_TakePicture, 0).ThrowIfEdSdkError("Could not take picture with AF");
            }
            else
            {
                EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely_NonAF).ThrowIfEdSdkError("Could not send Non-AF capture command");
                Thread.Sleep(100);
                EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF).ThrowIfEdSdkError("Could not send shutter release command");
            }
        });

        try
        {
            return await _takePictureCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("The camera didn't produce a JPG image within specified timeout. Check focus and camera settings");
        }
    }

    private uint OnCameraObject(uint inEvent, nint inRef, nint inContext)
    {
        if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer)
        {
            try
            {
                EDSDK.EdsGetDirectoryItemInfo(inRef, out var dirItemInfo).ThrowIfEdSdkError("Failed to get file info");

                if (Path.GetExtension(dirItemInfo.szFileName).ToLower() is not (".jpg" or ".jpeg"))
                    return 0;

                var tempFileName = $"{Path.GetTempFileName()}.jpg";
                EDSDK.EdsCreateFileStream(tempFileName, EDSDK.EdsFileCreateDisposition.CreateAlways, EDSDK.EdsAccess.ReadWrite, out var stream).ThrowIfEdSdkError("Failed to create download stream");

                try
                {
                    try
                    {
                        EDSDK.EdsDownload(inRef, dirItemInfo.Size, stream)
                            .ThrowIfEdSdkError($"Failed to download file: {dirItemInfo.szFileName}");
                        EDSDK.EdsDownloadComplete(inRef).ThrowIfEdSdkError("Failed to complete download");
                    }
                    finally
                    {
                        EDSDK.EdsRelease(stream);
                    }

                    var bytes = File.ReadAllBytes(tempFileName);
                    
                    try
                    {
                        File.Delete(tempFileName);
                    }
                    catch
                    {
                        // Ignore temp file deletion errors
                    }
                    
                    _takePictureCompletion?.SetResult(bytes);
                }
                catch (Exception exception)
                {
                    _takePictureCompletion?.SetException(exception);
                }
            }
            finally
            {
                EDSDK.EdsRelease(inRef);
                _takePictureCompletion = null;
            }
        }

        else if (inRef != nint.Zero) 
            EDSDK.EdsRelease(inRef);

        return 0;
    }

    public void Dispose()
    {
        _thread.Dispose();
    }
}