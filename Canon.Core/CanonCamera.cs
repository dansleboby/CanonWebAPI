using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace Canon.Core;

/// <summary>
/// Represents a Canon camera connected via the Canon EDSDK.
/// </summary>
public class CanonCamera : IDisposable
{
    private readonly Lock _initLock = new();
    private Task? _initTask;
    private nint _cameraRef;
    private readonly CanonThread _thread;

    // Private fields to avoid garbage collection issues with delegates
    private readonly EDSDK.EdsObjectEventHandler _onCameraObject;
    private readonly EDSDK.EdsStateEventHandler _onCameraStateChanged;
    private readonly EDSDK.EdsProgressCallback _onCameraProgress;
    private readonly EDSDK.EdsPropertyEventHandler _onCameraPropertyChanged;

    private readonly SemaphoreSlim _takePictureSemaphore = new(1, 1);
    private TaskCompletionSource<byte[]>? _takePictureCompletion;
    private byte[]? _latestImageBytes;

    public delegate void ProgressChangedHandler(uint percent, nint context, ref bool cancel);
    public delegate void PropertyChangedHandler(CameraProperty property, string value);

    public event PropertyChangedHandler? PropertyChanged;
    public event ProgressChangedHandler? ProgressChanged;

    public CanonCamera(ILogger? logger = null)
    {
        _thread = new CanonThread(logger);

        _onCameraObject = OnCameraObject;
        _onCameraStateChanged = OnCameraStateChanged;
        _onCameraPropertyChanged = OnCameraPropertyChanged;
        _onCameraProgress = OnCameraProgress;
    }

    private uint OnCameraPropertyChanged(uint inEvent, uint inPropertyId, uint inParam, nint inContext)
    {
        if (inEvent == EDSDK.PropertyEvent_PropertyChanged && Enum.IsDefined((CameraProperty)inPropertyId) && PropertyChanged != null)
        {
            Task.Run(async () =>
            {
                var value = await GetValue((CameraProperty)inPropertyId);
                Debug.WriteLine($"Prop: {(CameraProperty)inPropertyId} = {value}");
                PropertyChanged?.Invoke((CameraProperty)inPropertyId, value);
            });
        }

        return 0;
    }


    /// <summary>
    /// Handles camera state changes, such as shutdown events.
    /// </summary>
    private uint OnCameraStateChanged(uint inEvent, uint inParameter, IntPtr inContext)
    {
        if (inEvent == EDSDK.StateEvent_Shutdown)
        {
            lock (_initLock)
                _initTask = null;

            // If the camera shuts down, fail any pending picture task
            _takePictureCompletion?.TrySetException(new InvalidOperationException("Camera was disconnected or shut down."));
        }
        else if (inEvent == EDSDK.StateEvent_CaptureError)
        {
            // Camera failed to take the shot (e.g., focus failure)
            // This will immediately fail the TakePicture() task with a useful message
            string errorMsg = GetCaptureErrorMessage(inParameter);
            _takePictureCompletion?.TrySetException(new Exception(errorMsg)); // Or use your custom EdsException
        }

        return 0;
    }
    
    /// <summary>
    /// Helper method to translate capture error codes into messages.
    /// Based on EDSDK_API_EN.pdf Page 96
    /// </summary>
    private string GetCaptureErrorMessage(uint errorCode)
    {
        // --- THIS IS THE FIXED FUNCTION ---
        // Rewritten to use a classic switch statement for older C# compatibility
        switch (errorCode)
        {
            case 0x00000001:
                return "Shooting failure (General Error)";
            case 0x00000002:
                return "Lens cover was closed";
            case 0x00000003:
                return "General shooting error (Bulb or mirror-up)";
            case 0x00000004:
                return "Camera is busy cleaning the sensor";
            case 0x00000005:
                return "Camera is set to silent operation";
            case 0x00000006:
                return "No card inserted";
            case 0x00000007:
                return "Card error (Full or other)";
            case 0x00000008:
                return "Card write-protected";
            default:
                return $"Unknown capture error: 0x{errorCode:X}";
        }
    }

    /// <summary>
    /// Initializes the Canon SDK and connects to the first detected camera.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    private Task InitializeAsync()
    {
        lock (_initLock)
        {
            return _initTask ??= _thread.InvokeAsync(() =>
            {
                try
                {
                    EDSDK.EdsInitializeSDK().ThrowIfEdSdkError("Failed to initialize EDSDK");
                    EDSDK.EdsGetCameraList(out var cameraList).ThrowIfEdSdkError("Failed to get camera list");
                    EDSDK.EdsGetChildCount(cameraList, out var cameraCount).ThrowIfEdSdkError("Failed to get camera count");

                    try
                    {
                        if (cameraCount == 0)
                            throw new InvalidOperationException("No Canon camera detected");

                        EDSDK.EdsGetChildAtIndex(cameraList, 0, out _cameraRef).ThrowIfEdSdkError("Could not get first camera");
                    }
                    finally
                    {
                        EDSDK.EdsRelease(cameraList);
                    }

                    EDSDK.EdsOpenSession(_cameraRef).ThrowIfEdSdkError("Failed to open camera session");
                    EDSDK.EdsSetPropertyData(_cameraRef, EDSDK.PropID_SaveTo, 0, sizeof(uint), (uint)EDSDK.EdsSaveTo.Host).ThrowIfEdSdkError("Failed set SaveTo");
                    EDSDK.EdsSetCapacity(_cameraRef, new EDSDK.EdsCapacity { Reset = 1, BytesPerSector = 0x1000, NumberOfFreeClusters = 0x7FFFFFFF }).ThrowIfEdSdkError("Failed to set Capacity");
                    EDSDK.EdsSetPropertyData(_cameraRef, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), EDSDK.EvfOutputDevice_PC).ThrowIfEdSdkError("Failed to set EVF Output");
                    
                    EDSDK.EdsSetObjectEventHandler(_cameraRef, EDSDK.ObjectEvent_All, _onCameraObject, _cameraRef).ThrowIfEdSdkError("Failed to subscribe to ObjectEvent");
                    EDSDK.EdsSetCameraStateEventHandler(_cameraRef, EDSDK.StateEvent_All, _onCameraStateChanged, _cameraRef).ThrowIfEdSdkError("Failed to subscribe to StateEvent");
                    EDSDK.EdsSetPropertyEventHandler(_cameraRef, EDSDK.PropertyEvent_All, _onCameraPropertyChanged, _cameraRef).ThrowIfEdSdkError("Failed to subscribe to PropertyEvent");
                }
                catch(Exception ex)
                {
                    EDSDK.EdsTerminateSDK();
                    
                    lock (_initLock) 
                        _initTask = null;

                    throw new EdsException(EDSDK.EDS_ERR_DEVICE_INVALID, "Failed to initialize Canon camera", ex);
                }
            });
        }
    }

    private uint OnCameraProgress(uint inPercent, nint inContext, ref bool outCancel)
    {
        ProgressChanged?.Invoke(inPercent, inContext, ref outCancel);
        return 0;
    }

    /// <summary>
    /// Gets the connected camera's device name.
    /// </summary>
    public async Task<string> GetCameraName()
    {
        await InitializeAsync();

        return await _thread.InvokeAsync(() =>
        {
            EDSDK.EdsGetDeviceInfo(_cameraRef, out var info).ThrowIfEdSdkError("Could not get device info");
            return info.szDeviceDescription;
        });
    }

    /// <summary>
    /// Gets the value of a specific camera property. 
    /// </summary>
    public async Task<string> GetValue(CameraProperty property)
    {
        await InitializeAsync();
            
        return await _thread.InvokeAsync(() =>
        {
            var values = ((uint)property).GetPropertyValues();

            EDSDK.EdsGetPropertyData(_cameraRef, (uint)property, 0, out uint value).ThrowIfEdSdkError($"Could not get property 0x{property:X}");
            return values.TryGetValue(value, out var description) ? description : value.ToString();
        });
    }

    /// <summary>
    /// Gets a list of supported values for a specific camera property. 
    /// </summary>
    public async Task<List<string>> GetSupportedValues(CameraProperty propId)
    {
        await InitializeAsync();

        return await _thread.InvokeAsync(() =>
        {
            var values = ((uint)propId).GetPropertyValues();
            EDSDK.EdsGetPropertyDesc(_cameraRef, (uint)propId, out var desc).ThrowIfEdSdkError("EdsGetPropertyDesc failed");

            return Enumerable.Range(0, desc.NumElements)
                .Select(i => values.GetValueOrDefault((uint)desc.PropDesc[i]))
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();
        });
    }

    /// <summary>
    /// Sets the value of a specific camera property. 
    /// </summary>
    /// <param name="propId">The property ID to set</param>
    /// <param name="value">The value to set for the property</param>
    public async Task SetValue(CameraProperty propId, uint value)
    {
        await InitializeAsync();
        
        await _thread.InvokeAsync(() => EDSDK.EdsSetPropertyData(_cameraRef, (uint)propId, 0, sizeof(uint), value).ThrowIfEdSdkError($"Could not set property {propId} to {value}"));
    }

    public Task SetValue(CameraProperty propId, string description)
    {
        var descriptions = ((uint)propId).GetPropertyDescriptions();
        
        if (!descriptions.TryGetValue(description, out var value))
            throw new ArgumentOutOfRangeException($"Invalid value '{description}' for property {propId}");

        return SetValue(propId, value);
    }

    /// <summary>
    /// Gets the latest image bytes captured by the camera, if any
    /// </summary>
    public Task<byte[]?> GetLatestImageBytes() => Task.FromResult(_latestImageBytes);

    /// <summary>
    /// Sends an autofocus command to the camera.
    /// </summary>
    public async Task AutoFocus()
    {
        await InitializeAsync();
        await _takePictureSemaphore.WaitAsync();

        try
        {
            _takePictureCompletion = new TaskCompletionSource<byte[]>();

            await _thread.InvokeAsync(() =>
            {
                EDSDK.EdsSendCommand(_cameraRef, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Halfway).ThrowIfEdSdkError("Could not send AF command");
                Thread.Sleep(100); 
                EDSDK.EdsSendCommand(_cameraRef, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF).ThrowIfEdSdkError("Could not send shutter release command");
            });
        }
        finally
        {
            _takePictureSemaphore.Release();
        }
    }

    /// <summary>
    /// Takes a picture with the camera, optionally using autofocus.
    /// </summary>
    /// <param name="useAutoFocus"></param>
    /// <returns></returns>
    /// <exception cref="TimeoutException"></exception>
    public async Task<byte[]?> TakePicture(bool useAutoFocus = true)
    {
        await InitializeAsync();
        await _takePictureSemaphore.WaitAsync();

        try
        {
            _takePictureCompletion = new TaskCompletionSource<byte[]>();

            await _thread.InvokeAsync(() =>
            {
                if (useAutoFocus)
                {
                    EDSDK.EdsSendCommand(_cameraRef, EDSDK.CameraCommand_TakePicture, 0).ThrowIfEdSdkError("Could not take picture with AF");
                }
                else
                {
                    EDSDK.EdsSendCommand(_cameraRef, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely_NonAF).ThrowIfEdSdkError("Could not send Non-AF capture command");
                    Thread.Sleep(100);
                    EDSDK.EdsSendCommand(_cameraRef, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF).ThrowIfEdSdkError("Could not send shutter release command");
                }
            });

            try
            {
                // Wait for OnCameraObject or OnCameraStateChanged to complete the task
                return await _takePictureCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                // Invalidate the TCS so the late event doesn't complete a *future* request
                _takePictureCompletion = null; 
                
                // We can also add a better message, as you wanted
                throw new TimeoutException("Picture taking operation timed out. The camera did not respond. Check focus, lens cap, or if it's set to RAW.");
            }
        }
        finally
        {
            _takePictureSemaphore.Release();
        }
    }

    /// <summary>
    /// Gets the live view image from the camera.
    /// </summary>
    public async Task<byte[]?> GetLiveView()
    {
        await InitializeAsync();

        // Try to acquire the semaphore with a 0ms timeout.
        if (!await _takePictureSemaphore.WaitAsync(0))
        {
            // If this fails, TakePicture is busy. Skip this frame.
            return null;
        }

        try
        {
            return await _thread.InvokeAsync(() =>
            {
                var evfImage = nint.Zero;
                var stream = nint.Zero;

                try
                {
                    // 1. Create Stream
                    EDSDK.EdsCreateMemoryStream(0, out stream).ThrowIfEdSdkError("Could not create memory stream for EVF image");
                    if (stream == nint.Zero) throw new EdsException(0, "EdsCreateMemoryStream returned OK but stream was null.", null);

                    // 2. Create Image Ref
                    EDSDK.EdsCreateEvfImageRef(stream, out evfImage).ThrowIfEdSdkError("Could not create EVF image reference");
                    if (evfImage == nint.Zero) throw new EdsException(0, "EdsCreateEvfImageRef returned OK but image ref was null.", null);
                    
                    // 3. Download Image
                    var err = EDSDK.EdsDownloadEvfImage(_cameraRef, evfImage);
                    if (err == EDSDK.EDS_ERR_OBJECT_NOTREADY)
                    {
                        return null; // Not an error, just not ready for a frame
                    }
                    err.ThrowIfEdSdkError("Could not download EVF image"); // Throws on other errors

                    // 4. Get Pointer (Defensive Check 1)
                    // This is the area where the NullReferenceException was happening
                    var ptrErr = EDSDK.EdsGetPointer(stream, out var imagePtr);
                    if (ptrErr != EDSDK.EDS_ERR_OK || imagePtr == nint.Zero)
                    {
                        // Camera said download was OK, but the stream pointer is bad.
                        // This happens when the camera is in a bad state. Skip the frame.
                        return null; 
                    }

                    // 5. Get Length (Defensive Check 2)
                    var lenErr = EDSDK.EdsGetLength(stream, out var length);
                    if (lenErr != EDSDK.EDS_ERR_OK || length == 0)
                    {
                        // Stream is bad or empty, skip frame
                        return null;
                    }

                    // 6. Copy bytes
                    var bytes = new byte[length];
                    Marshal.Copy(imagePtr, bytes, 0, (int)length);
                    return bytes;
                }
                finally
                {
                    // This release code is correct and essential
                    if (stream != nint.Zero) EDSDK.EdsRelease(stream);
                    if (evfImage != nint.Zero) EDSDK.EdsRelease(evfImage);
                }
            });
        }
        finally
        {
            // Always release the semaphore so the next operation can run
            _takePictureSemaphore.Release();
        }
    }

    /// <summary>
    /// Handles camera object events, such as file transfers. 
    /// </summary>
    private uint OnCameraObject(uint inEvent, nint inRef, nint inContext)
    {
        // This event is triggered when an image is ready to be downloaded
        if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer)
        {
            try
            {
                EDSDK.EdsGetDirectoryItemInfo(inRef, out var dirItemInfo).ThrowIfEdSdkError("Failed to get file info");

                // We only care about JPGs for the TakePicture preview
                if (Path.GetExtension(dirItemInfo.szFileName).ToLower() is not (".jpg" or ".jpeg"))
                {
                    // This wasn't the image we wanted, just complete the download and ignore
                    EDSDK.EdsDownloadComplete(inRef).ThrowIfEdSdkError("Failed to complete non-JPG download");
                    return 0;
                }

                var tempFileName = $"{Path.GetTempFileName()}.jpg";
                EDSDK.EdsCreateFileStream(tempFileName, EDSDK.EdsFileCreateDisposition.CreateAlways, EDSDK.EdsAccess.ReadWrite, out var stream).ThrowIfEdSdkError("Failed to create download stream");

                try
                {
                    try
                    {
                        EDSDK.EdsDownload(inRef, dirItemInfo.Size, stream).ThrowIfEdSdkError($"Failed to download file: {dirItemInfo.szFileName}");
                        EDSDK.EdsSetProgressCallback(stream, _onCameraProgress, EDSDK.EdsProgressOption.Periodically, stream).ThrowIfEdSdkError("Failed to register download progress");
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
 
                    _latestImageBytes = bytes;
                    
                    // Use TrySetResult for race-condition safety
                    _takePictureCompletion?.TrySetResult(bytes);
                }
                catch (Exception exception)
                {
                    _takePictureCompletion?.TrySetException(exception);
                }
                finally
                {
                    // Clear the TCS now that this transfer is handled (success or fail)
                    _takePictureCompletion = null;
                }
            }
            finally
            {
                // Always release the event reference
                if (inRef != nint.Zero) 
                    EDSDK.EdsRelease(inRef);
            }
        }
        else if (inRef != nint.Zero) 
        {
            // Release other events we don't care about
            EDSDK.EdsRelease(inRef);
        }

        return 0;
    }

    /// <summary>
    /// Releases all resources used by the Canon camera instance. 
    /// </summary>
    public void Dispose()
    {
        _thread.Dispose();
    }
}