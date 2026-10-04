using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Canon.Core;

/// <summary>
/// Represents a Canon camera connected via the Canon EDSDK.
/// </summary>
/// <remarks>
/// The SDK is initialized once for the lifetime of the instance. The connection to the camera is opened on demand,
/// closed when the camera is turned off or unplugged, and opened again automatically when it comes back
/// (hot plug through <c>EdsSetCameraAddedHandler</c>, or on the next call).
/// Every SDK call runs on a dedicated thread (<see cref="CanonThread"/>).
/// </remarks>
public sealed class CanonCamera : IDisposable
{
    private sealed class PendingCapture(IReadOnlySet<string> fileTypes)
    {
        public IReadOnlySet<string> FileTypes { get; } = fileTypes;
        public TaskCompletionSource<CapturedImage> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly CanonThread _thread;
    private readonly ILogger? _logger;
    private readonly CanonCameraOptions _options;
    private readonly IReadOnlySet<string> _defaultFileTypes;

    // Delegates are kept in fields so the garbage collector does not collect them while the SDK holds pointers to them.
    private readonly EDSDK.EdsObjectEventHandler _onCameraObject;
    private readonly EDSDK.EdsStateEventHandler _onCameraStateChanged;
    private readonly EDSDK.EdsProgressCallback _onCameraProgress;
    private readonly EDSDK.EdsPropertyEventHandler _onCameraPropertyChanged;
    private readonly EDSDK.EdsCameraAddedHandler _onCameraAdded;

    // Only accessed on the Canon thread.
    private bool _sdkInitialized;
    private nint _cameraRef;
    private nint _flashRef;
    private bool? _flashFiring;
    private volatile string? _flashError;

    private volatile bool _connected;
    private volatile bool _liveViewActive;
    private volatile bool _liveViewRequested;
    private volatile bool _capturing;
    private volatile PendingCapture? _pendingCapture;
    private volatile CapturedImage? _latestImage;

    // kEdsStateEvent_JobStatusChanged: the camera still has files to transfer.
    private volatile bool _transferJobPending;
    // Set when a capture timed out: files arriving until the camera is idle belong to that capture, not the next one.
    private volatile bool _staleTransfers;

    private readonly Lock _connectLock = new();
    private Task? _connectTask;
    private readonly SemaphoreSlim _shutterLock = new(1, 1);
    private int _disposed;

    public delegate void ProgressChangedHandler(uint percent, nint context, ref bool cancel);
    public delegate void PropertyChangedHandler(CameraProperty property, string value);

    /// <summary>
    /// Raised when one of the <see cref="CameraProperty"/> values changes on the camera.
    /// </summary>
    public event PropertyChangedHandler? PropertyChanged;

    /// <summary>
    /// Raised while a captured file is downloaded.
    /// </summary>
    public event ProgressChangedHandler? ProgressChanged;

    /// <summary>
    /// Raised when the camera gets connected (true) or disconnected (false).
    /// </summary>
    public event Action<bool>? ConnectionChanged;

    public CanonCamera(ILogger? logger = null, CanonCameraOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new CanonCameraOptions();
        _defaultFileTypes = CaptureFileTypes.Normalize(_options.CaptureFileTypes);
        if (_defaultFileTypes.Count == 0)
            _defaultFileTypes = CaptureFileTypes.Normalize(["jpg"]);

        // Events are only pumped once the SDK is initialized.
        _thread = new CanonThread(logger, () =>
        {
            if (_sdkInitialized)
                EDSDK.EdsGetEvent();
        });

        _onCameraObject = OnCameraObject;
        _onCameraStateChanged = OnCameraStateChanged;
        _onCameraPropertyChanged = OnCameraPropertyChanged;
        _onCameraProgress = OnCameraProgress;
        _onCameraAdded = OnCameraAdded;
    }

    /// <summary>
    /// True while a session is open with the camera.
    /// </summary>
    public bool IsConnected => _connected;

    /// <summary>
    /// True while the camera streams its live view to the PC.
    /// </summary>
    public bool IsLiveViewActive => _liveViewActive;

    #region Connection

    /// <summary>
    /// Connects to the first detected camera, if not already connected.
    /// </summary>
    /// <exception cref="CameraNotConnectedException">No camera is detected.</exception>
    public async Task ConnectAsync()
    {
        if (_connected)
            return;

        Task task;
        lock (_connectLock)
            task = _connectTask ??= _thread.InvokeAsync(ConnectCore);

        try
        {
            await task;
        }
        finally
        {
            lock (_connectLock)
            {
                if (_connectTask == task)
                    _connectTask = null;
            }
        }
    }

    /// <summary>
    /// Initializes the SDK. Must be called once per process (EDSDK API reference 2.9); terminated in <see cref="Dispose"/>.
    /// </summary>
    private void EnsureSdkInitialized()
    {
        if (_sdkInitialized)
            return;

        EDSDK.EdsInitializeSDK().ThrowIfEdSdkError("Failed to initialize EDSDK");
        _sdkInitialized = true;

        var err = EDSDK.EdsSetCameraAddedHandler(_onCameraAdded, nint.Zero);
        if (err != EDSDK.EDS_ERR_OK)
            _logger?.LogWarning("Could not register the camera added handler (0x{Error:X}): hot plug is disabled", err);

        _logger?.LogInformation("EDSDK initialized");
    }

    private void ConnectCore()
    {
        EnsureSdkInitialized();

        if (_cameraRef != nint.Zero)
            return;

        EDSDK.EdsGetCameraList(out var cameraList).ThrowIfEdSdkError("Failed to get camera list");
        nint camera;

        try
        {
            EDSDK.EdsGetChildCount(cameraList, out var cameraCount).ThrowIfEdSdkError("Failed to get camera count");

            if (cameraCount == 0)
                throw new CameraNotConnectedException("No Canon camera detected");

            EDSDK.EdsGetChildAtIndex(cameraList, 0, out camera).ThrowIfEdSdkError("Could not get first camera");
        }
        finally
        {
            EDSDK.EdsRelease(cameraList);
        }

        var sessionOpened = false;

        try
        {
            // Private properties must be enabled before opening the session (EDSDK API reference 6.9 and 6.11).
            EDSDK.EdsSetPropertyData(camera, EDSDK.PropID_EnablePrivateProperty, EDSDK.EnablePrivateProperty_TempStatus, sizeof(uint), EDSDK.PropID_TempStatus);
            EDSDK.EdsSetPropertyData(camera, EDSDK.PropID_EnablePrivateProperty, EDSDK.EnablePrivateProperty_FixedMovie, sizeof(uint), EDSDK.PropID_FixedMovie);

            // Handlers are registered before the session is opened, as in the SDK samples, so no event is missed.
            EDSDK.EdsSetObjectEventHandler(camera, EDSDK.ObjectEvent_All, _onCameraObject, nint.Zero).ThrowIfEdSdkError("Failed to subscribe to ObjectEvent");
            EDSDK.EdsSetCameraStateEventHandler(camera, EDSDK.StateEvent_All, _onCameraStateChanged, nint.Zero).ThrowIfEdSdkError("Failed to subscribe to StateEvent");
            EDSDK.EdsSetPropertyEventHandler(camera, EDSDK.PropertyEvent_All, _onCameraPropertyChanged, nint.Zero).ThrowIfEdSdkError("Failed to subscribe to PropertyEvent");

            EDSDK.EdsOpenSession(camera).ThrowIfEdSdkError("Failed to open camera session");
            sessionOpened = true;

            // Pictures are transferred to the PC (the photo booth camera has no memory card).
            var err = EDSDK.EdsSetPropertyData(camera, EDSDK.PropID_SaveTo, 0, sizeof(uint), (uint)EDSDK.EdsSaveTo.Host);
            if (err == EDSDK.EDS_ERR_OK)
                err = EDSDK.EdsSetCapacity(camera, new EDSDK.EdsCapacity { Reset = 1, BytesPerSector = 0x1000, NumberOfFreeClusters = 0x7FFFFFFF });
            if (err != EDSDK.EDS_ERR_OK)
                _logger?.LogWarning("Could not set the PC as the save destination: {Error}", EdsdkHelper.GetErrorMessage(err));
        }
        catch
        {
            if (sessionOpened)
                EDSDK.EdsCloseSession(camera);
            EDSDK.EdsRelease(camera);
            throw;
        }

        _cameraRef = camera;
        _connected = true;
        _liveViewActive = false;

        InitializeFlash(camera);

        EDSDK.EdsGetDeviceInfo(camera, out var info);
        _logger?.LogInformation("Connected to camera {Camera}", info.szDeviceDescription);
        RaiseConnectionChanged(true);
    }

    /// <summary>
    /// Closes the session and releases the camera. Runs on the Canon thread.
    /// </summary>
    /// <param name="closeSession">
    /// False when the camera is gone: releasing it closes the session, and closing it before makes the Linux EDSDK
    /// terminate the USB connection twice (crash in EdsRelease). The Canon samples only release it too.
    /// </param>
    private void DisconnectCore(string reason, nint expectedCamera = 0, bool closeSession = true)
    {
        var camera = _cameraRef;

        // The session that failed may already have been replaced by a new one: keep the new one.
        if (expectedCamera != nint.Zero && camera != expectedCamera)
            return;

        _connected = false;
        _liveViewActive = false;

        _pendingCapture?.Completion.TrySetException(new CameraNotConnectedException($"Camera disconnected: {reason}"));

        if (camera == nint.Zero)
            return;

        _cameraRef = nint.Zero;

        if (_flashRef != nint.Zero)
        {
            EDSDK.EdsRelease(_flashRef);
            _flashRef = nint.Zero;
        }
        _flashFiring = null;

        if (closeSession)
            EDSDK.EdsCloseSession(camera);
        EDSDK.EdsRelease(camera);

        _logger?.LogWarning("Camera disconnected: {Reason}", reason);
        RaiseConnectionChanged(false);
    }

    private void RaiseConnectionChanged(bool connected)
    {
        var handler = ConnectionChanged;
        if (handler != null)
            Task.Run(() => handler(connected));
    }

    private uint OnCameraAdded(nint inContext)
    {
        _logger?.LogInformation("A camera was plugged in");

        if (!_connected && Volatile.Read(ref _disposed) == 0)
        {
            Task.Run(async () =>
            {
                try
                {
                    await ConnectPluggedCameraAsync();

                    if (_liveViewRequested)
                        await StartLiveViewAsync();
                }
                catch (Exception e)
                {
                    _logger?.LogWarning(e, "Could not connect to the plugged camera");
                }
            });
        }

        return EDSDK.EDS_ERR_OK;
    }

    /// <summary>
    /// Connects to a camera that was just plugged in, retrying for about 10 s: on Linux the SDK lists the camera
    /// a few seconds after raising the camera added event.
    /// </summary>
    private async Task ConnectPluggedCameraAsync()
    {
        const int attempts = 20;

        for (var attempt = 1; ; attempt++)
        {
            // Give the camera a moment to be ready before opening the session.
            await Task.Delay(500);

            try
            {
                await ConnectAsync();
                _logger?.LogDebug("Plugged camera connected (attempt {Attempt})", attempt);
                return;
            }
            catch (EdsException) when (attempt < attempts && Volatile.Read(ref _disposed) == 0)
            {
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="func"/> on the Canon thread with the connected camera.
    /// </summary>
    private async Task<T> RunAsync<T>(Func<nint, T> func)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await ConnectAsync();

        var usedCamera = nint.Zero;

        try
        {
            return await _thread.InvokeAsync(() =>
            {
                usedCamera = _cameraRef;
                if (usedCamera == nint.Zero)
                    throw new CameraNotConnectedException("Camera is not connected");

                return func(usedCamera);
            });
        }
        catch (EdsException e) when (e.IsDisconnected && e is not CameraNotConnectedException && usedCamera != nint.Zero)
        {
            // The session is no longer usable: drop it so the next call reconnects.
            await _thread.InvokeAsync(() => DisconnectCore(e.Message, usedCamera, closeSession: false));
            throw;
        }
    }

    private Task RunAsync(Action<nint> action) => RunAsync(camera =>
    {
        action(camera);
        return true;
    });

    /// <summary>
    /// Same as <see cref="RunAsync{T}"/>, retried when the camera answers "device busy" (as in the SDK samples).
    /// </summary>
    private async Task<T> RunWithBusyRetryAsync<T>(Func<nint, T> func, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RunAsync(func);
            }
            catch (EdsException e) when (e.IsBusy && attempt < Math.Max(1, _options.BusyRetryCount))
            {
                _logger?.LogDebug("Camera busy, retrying ({Attempt})", attempt);
                await Task.Delay(_options.BusyRetryDelayMilliseconds, cancellationToken);
            }
        }
    }

    private Task RunWithBusyRetryAsync(Action<nint> action, CancellationToken cancellationToken = default) =>
        RunWithBusyRetryAsync(camera =>
        {
            action(camera);
            return true;
        }, cancellationToken);

    /// <summary>
    /// Runs an SDK call and logs its result and duration at Debug level.
    /// </summary>
    private uint TraceCall(string step, Func<uint> call)
    {
        if (_logger is not { } logger || !logger.IsEnabled(LogLevel.Debug))
            return call();

        var start = Stopwatch.GetTimestamp();
        var result = call();
        logger.LogDebug("{Step}: {Result} ({Duration:0} ms)", step, EdsdkHelper.DescribeResult(result), Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return result;
    }

    #endregion

    #region Event handlers

    private uint OnCameraPropertyChanged(uint inEvent, uint inPropertyId, uint inParam, nint inContext)
    {
        _logger?.LogDebug("SDK event {Event}: property 0x{Property:X}, parameter 0x{Parameter:X}, thread {Thread}", EdsdkHelper.DescribeEvent(inEvent), inPropertyId, inParam, Environment.CurrentManagedThreadId);

        if (inEvent != EDSDK.PropertyEvent_PropertyChanged || PropertyChanged == null)
            return EDSDK.EDS_ERR_OK;

        // 0x0000FFFF means the changed property cannot be identified: refresh all of them (EDSDK API reference 4.2.2).
        CameraProperty[] properties = inPropertyId == EDSDK.PropID_Unknown
            ? Enum.GetValues<CameraProperty>()
            : Enum.IsDefined((CameraProperty)inPropertyId) ? [(CameraProperty)inPropertyId] : [];

        foreach (var property in properties)
        {
            Task.Run(async () =>
            {
                try
                {
                    var value = await GetValue(property);
                    PropertyChanged?.Invoke(property, value);
                }
                catch (Exception e)
                {
                    _logger?.LogDebug(e, "Could not read {Property} after a change notification", property);
                }
            });
        }

        return EDSDK.EDS_ERR_OK;
    }

    /// <summary>
    /// Handles camera state changes (EDSDK API reference 4.2).
    /// </summary>
    private uint OnCameraStateChanged(uint inEvent, uint inParameter, nint inContext)
    {
        _logger?.LogDebug("SDK event {Event}: parameter 0x{Parameter:X}, thread {Thread}", EdsdkHelper.DescribeEvent(inEvent), inParameter, Environment.CurrentManagedThreadId);

        switch (inEvent)
        {
            case EDSDK.StateEvent_Shutdown:
                // SDK calls are deferred until the callback has returned.
                _connected = false;
                _thread.Post(() => DisconnectCore("camera turned off or unplugged", closeSession: false));
                break;

            case EDSDK.StateEvent_WillSoonShutDown:
                _logger?.LogInformation("Camera will turn off in {Seconds} s", inParameter);
                if (_options.PreventAutoPowerOff)
                {
                    _thread.Post(() =>
                    {
                        if (_cameraRef != nint.Zero)
                            EDSDK.EdsSendCommand(_cameraRef, EDSDK.CameraCommand_ExtendShutDownTimer, 0);
                    });
                }
                break;

            case EDSDK.StateEvent_JobStatusChanged:
                _transferJobPending = inParameter != 0;
                if (!_transferJobPending)
                    _staleTransfers = false;
                break;

            case EDSDK.StateEvent_CaptureError:
                // The camera failed to take the shot (e.g. focus failure).
                _pendingCapture?.Completion.TrySetException(new CaptureFailedException(inParameter));
                break;

            case EDSDK.StateEvent_InternalError:
                // The camera will probably not work properly anymore: drop the connection (EDSDK API reference 4.2.18).
                _logger?.LogError("EDSDK internal error 0x{Error:X}", inParameter);
                _thread.Post(() => DisconnectCore("EDSDK internal error"));
                break;
        }

        return EDSDK.EDS_ERR_OK;
    }

    private uint OnCameraProgress(uint inPercent, nint inContext, ref bool outCancel)
    {
        ProgressChanged?.Invoke(inPercent, inContext, ref outCancel);
        return EDSDK.EDS_ERR_OK;
    }

    /// <summary>
    /// Handles camera object events, such as file transfer requests.
    /// </summary>
    private uint OnCameraObject(uint inEvent, nint inRef, nint inContext)
    {
        _logger?.LogDebug("SDK event {Event}, thread {Thread}", EdsdkHelper.DescribeEvent(inEvent), Environment.CurrentManagedThreadId);

        try
        {
            if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer && inRef != nint.Zero)
                TransferFile(inRef);
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "Error while handling object event 0x{Event:X}", inEvent);
        }
        finally
        {
            if (inRef != nint.Zero)
                EDSDK.EdsRelease(inRef);
        }

        return EDSDK.EDS_ERR_OK;
    }

    /// <summary>
    /// Downloads a file the camera asks to transfer, or cancels the transfer when the file type is not wanted.
    /// Every transfer request must end with EdsDownloadComplete or EdsDownloadCancel (EDSDK API reference 4.2.12).
    /// </summary>
    private void TransferFile(nint dirItem)
    {
        // A late file of a timed out capture must not answer the next capture.
        var pending = _staleTransfers ? null : _pendingCapture;

        try
        {
            var err = EDSDK.EdsGetDirectoryItemInfo(dirItem, out var info);
            if (err != EDSDK.EDS_ERR_OK)
            {
                EDSDK.EdsDownloadCancel(dirItem);
                err.ThrowIfEdSdkError("Failed to get file info");
            }

            _logger?.LogDebug("Transfer requested: {File} ({Size} bytes), pending capture: {Pending}", info.szFileName, info.Size, pending != null);

            if (!CaptureFileTypes.Matches(info.szFileName, pending?.FileTypes ?? _defaultFileTypes))
            {
                _logger?.LogDebug("Skipping {File}: file type not requested", info.szFileName);
                TraceCall("Download cancel", () => EDSDK.EdsDownloadCancel(dirItem));
                return;
            }

            var bytes = DownloadToMemory(dirItem, info);
            var image = new CapturedImage(bytes, info.szFileName, CaptureFileTypes.GetContentType(info.szFileName));
            _logger?.LogInformation("Downloaded {File} ({Size} bytes)", info.szFileName, bytes.Length);

            // With several requested file types, the first file received answers the capture.
            if (pending == null || pending.Completion.TrySetResult(image))
                _latestImage = image;
        }
        catch (Exception e)
        {
            pending?.Completion.TrySetException(e);
            if (pending == null)
                throw;
        }
    }

    private byte[] DownloadToMemory(nint dirItem, EDSDK.EdsDirectoryItemInfo info)
    {
        var err = EDSDK.EdsCreateMemoryStream(0, out var stream);
        if (err != EDSDK.EDS_ERR_OK)
        {
            EDSDK.EdsDownloadCancel(dirItem);
            err.ThrowIfEdSdkError("Could not create download stream");
        }

        try
        {
            // The progress callback must be registered before EdsDownload to be called during the transfer.
            EDSDK.EdsSetProgressCallback(stream, _onCameraProgress, EDSDK.EdsProgressOption.Periodically, nint.Zero);

            err = TraceCall("Download", () => EDSDK.EdsDownload(dirItem, info.Size, stream));
            if (err != EDSDK.EDS_ERR_OK)
            {
                EDSDK.EdsDownloadCancel(dirItem);
                err.ThrowIfEdSdkError($"Failed to download file {info.szFileName}");
            }

            TraceCall("Download complete", () => EDSDK.EdsDownloadComplete(dirItem)).ThrowIfEdSdkError("Failed to complete download");

            return CopyStream(stream);
        }
        finally
        {
            EDSDK.EdsRelease(stream);
        }
    }

    private static byte[] CopyStream(nint stream)
    {
        EDSDK.EdsGetLength(stream, out var length).ThrowIfEdSdkError("Could not get stream length");
        if (length == 0)
            return [];

        EDSDK.EdsGetPointer(stream, out var pointer).ThrowIfEdSdkError("Could not get stream pointer");
        if (pointer == nint.Zero)
            throw new EdsException(EDSDK.EDS_ERR_INVALID_POINTER, "Stream pointer is null");

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, (int)length);
        return bytes;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the connected camera's device name.
    /// </summary>
    public Task<string> GetCameraName() => RunAsync(camera =>
    {
        EDSDK.EdsGetDeviceInfo(camera, out var info).ThrowIfEdSdkError("Could not get device info");
        return info.szDeviceDescription;
    });

    /// <summary>
    /// Gets the value of a specific camera property, as a label (e.g. "1/125"), or a raw value ("0x93") when unknown.
    /// </summary>
    public Task<string> GetValue(CameraProperty property) => RunAsync(camera =>
    {
        EDSDK.EdsGetPropertyData(camera, (uint)property, 0, out uint value).ThrowIfEdSdkError($"Could not get {property}");
        return ((uint)property).DescribeValue(value);
    });

    /// <summary>
    /// Gets the values that can currently be set for a specific camera property (empty when the camera does not list them).
    /// </summary>
    public Task<List<string>> GetSupportedValues(CameraProperty property) => RunAsync(camera =>
        (GetSettableValues(camera, property) ?? [])
            .Select(value => ((uint)property).DescribeValue(value))
            .ToList());

    /// <summary>
    /// Values that can currently be set (EdsGetPropertyDesc), or null when the camera does not list them for this property.
    /// </summary>
    private static uint[]? GetSettableValues(nint camera, CameraProperty property)
    {
        var err = EDSDK.EdsGetPropertyDesc(camera, (uint)property, out var desc);

        // EdsGetPropertyDesc is only documented for some properties (EDSDK API reference 3.1.20).
        if (err is EDSDK.EDS_ERR_INVALID_PARAMETER or EDSDK.EDS_ERR_NOT_SUPPORTED or EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE or EDSDK.EDS_ERR_DEVICEPROP_NOT_SUPPORTED)
            return null;

        err.ThrowIfEdSdkError($"Could not get the supported values of {property}");

        return (desc.PropDesc ?? [])
            .Take(Math.Clamp(desc.NumElements, 0, desc.PropDesc?.Length ?? 0))
            .Select(value => (uint)value)
            .ToArray();
    }

    /// <summary>
    /// Sets the raw value of a specific camera property, with the same checks as <see cref="SetValue(CameraProperty, string)"/>.
    /// </summary>
    public Task SetValue(CameraProperty property, uint value) => SetValue(property, EdsdkHelper.FormatRawValue(value));

    /// <summary>
    /// Sets a camera property from its label (e.g. "1/125", "5.6", "Auto") or raw value ("0x93").
    /// The value is checked first: readable label, setting changeable in the current mode, value accepted by the camera now.
    /// </summary>
    /// <exception cref="CameraSettingsException">The value is refused; nothing was written.</exception>
    /// <exception cref="EdsException">The camera refused the value when it was written.</exception>
    public async Task SetValue(CameraProperty property, string description)
    {
        try
        {
            await ApplySettingsAsync(new Dictionary<CameraProperty, string> { [property] = description });
        }
        catch (CameraSettingsApplyException e) when (e.InnerException != null)
        {
            // A single setting: report the camera error itself, as before.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }

    /// <summary>
    /// Reads the shooting mode and the ISO, aperture, shutter speed, exposure compensation and white balance settings in one call.
    /// </summary>
    public Task<CameraSettings> GetSettingsAsync() => RunWithBusyRetryAsync(camera =>
    {
        var mode = ReadMode(camera);
        var isoAuto = IsIsoAuto(camera);

        CameraSettingState Read(CameraProperty property)
        {
            EDSDK.EdsGetPropertyData(camera, (uint)property, 0, out uint value).ThrowIfEdSdkError($"Could not get {property}");
            var settable = GetSettableValues(camera, property);
            var supported = (settable ?? []).Select(v => ((uint)property).DescribeValue(v)).ToList();
            return new CameraSettingState(((uint)property).DescribeValue(value), supported, CameraSettingRules.IsSettable(property, mode.AEModeCode, settable, isoAuto));
        }

        return new CameraSettings(mode, Read(CameraProperty.ISOSpeed), Read(CameraProperty.Aperture), Read(CameraProperty.ShutterSpeed),
            Read(CameraProperty.ExposureCompensation), Read(CameraProperty.WhiteBalance));
    });

    /// <summary>
    /// Sets several camera properties. Every value is checked before anything is written (readable label, setting
    /// changeable in the current mode, value accepted by the camera now); the values are then written in
    /// <see cref="CameraSettingRules.WriteOrder"/>, never during a capture or an autofocus.
    /// </summary>
    /// <exception cref="CameraSettingsException">At least one value is refused; nothing was written.</exception>
    /// <exception cref="CameraSettingsApplyException">A value could not be written; the previous ones were.</exception>
    public async Task ApplySettingsAsync(IReadOnlyDictionary<CameraProperty, string> settings, CancellationToken cancellationToken = default)
    {
        if (settings.Count == 0)
            return;

        var ordered = CameraSettingRules.WriteOrder.Where(settings.ContainsKey)
            .Concat(settings.Keys.Where(p => !CameraSettingRules.WriteOrder.Contains(p)))
            .ToList();

        await _shutterLock.WaitAsync(cancellationToken);

        try
        {
            var values = await RunWithBusyRetryAsync(camera =>
            {
                var mode = ReadMode(camera);
                var isoAuto = IsIsoAutoAfter(camera, settings);
                var errors = new List<SettingError>();
                var valid = new List<(CameraProperty Property, uint Value)>();

                foreach (var property in ordered)
                {
                    var error = CameraSettingRules.Validate(property, settings[property], mode.AEModeCode, GetSettableValues(camera, property), out var value, isoAuto);
                    if (error != null)
                        errors.Add(error);
                    else
                        valid.Add((property, value));
                }

                if (errors.Count > 0)
                    throw new CameraSettingsException(mode, errors);

                return valid;
            }, cancellationToken);

            var applied = new List<CameraProperty>();

            foreach (var (property, value) in values)
            {
                try
                {
                    await RunWithBusyRetryAsync(camera =>
                        EDSDK.EdsSetPropertyData(camera, (uint)property, 0, sizeof(uint), value)
                            .ThrowIfEdSdkError($"Could not set {property} to {((uint)property).DescribeValue(value)}"), cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw new CameraSettingsApplyException(applied, property, settings[property], e);
                }

                applied.Add(property);
            }
        }
        finally
        {
            _shutterLock.Release();
        }
    }

    /// <summary>
    /// Whether ISO is Auto once <paramref name="settings"/> are applied: the requested ISO when readable, the current one otherwise.
    /// </summary>
    private static bool IsIsoAutoAfter(nint camera, IReadOnlyDictionary<CameraProperty, string> settings) =>
        settings.TryGetValue(CameraProperty.ISOSpeed, out var requested) && ((uint)CameraProperty.ISOSpeed).TryParseValue(requested, out var requestedIso)
            ? requestedIso == CameraSettingRules.IsoAuto
            : IsIsoAuto(camera);

    private static bool IsIsoAuto(nint camera) =>
        EDSDK.EdsGetPropertyData(camera, (uint)CameraProperty.ISOSpeed, 0, out uint iso) == EDSDK.EDS_ERR_OK && iso == CameraSettingRules.IsoAuto;

    /// <summary>
    /// Gets the shooting mode of the camera (mode dial position and still/movie mode).
    /// </summary>
    public Task<CameraMode> GetMode() => RunAsync(ReadMode);

    private static CameraMode ReadMode(nint camera)
    {
        EDSDK.EdsGetPropertyData(camera, EDSDK.PropID_AEMode, 0, out uint aeMode).ThrowIfEdSdkError("Could not get the AE mode");

        bool? isMovieMode = EDSDK.EdsGetPropertyData(camera, EDSDK.PropID_FixedMovie, 0, out uint fixedMovie) == EDSDK.EDS_ERR_OK
            ? fixedMovie != 0
            : null;

        return new CameraMode(aeMode, CameraSettingRules.DescribeAEMode(aeMode), EdsdkHelper.CreativeZoneAEModes.Contains(aeMode), isMovieMode);
    }

    /// <summary>
    /// Gets the restrictions applied by the camera because of its internal temperature.
    /// </summary>
    public Task<TemperatureStatus> GetTemperatureStatus() => RunAsync(camera =>
    {
        var err = EDSDK.EdsGetPropertyData(camera, EDSDK.PropID_TempStatus, 0, out uint value);

        if (err is EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE or EDSDK.EDS_ERR_NOT_SUPPORTED or EDSDK.EDS_ERR_DEVICEPROP_NOT_SUPPORTED or EDSDK.EDS_ERR_INVALID_PARAMETER)
            return TemperatureStatus.Unsupported;

        err.ThrowIfEdSdkError("Could not get the temperature status");
        return TemperatureStatus.FromRawValue(value);
    });

    #endregion

    #region Shooting

    /// <summary>
    /// Gets the latest image bytes captured by the camera, if any.
    /// </summary>
    public Task<byte[]?> GetLatestImageBytes() => Task.FromResult(_latestImage?.Data);

    /// <summary>
    /// Gets the latest file captured by the camera, if any.
    /// </summary>
    public CapturedImage? GetLatestImage() => _latestImage;

    /// <summary>
    /// Presses the shutter button halfway to focus, then releases it.
    /// </summary>
    public async Task AutoFocus()
    {
        await _shutterLock.WaitAsync();

        try
        {
            await RunWithBusyRetryAsync(camera =>
                TraceCall("Press shutter button halfway", () => EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Halfway))
                    .ThrowIfEdSdkError("Could not send AF command"));

            try
            {
                // The halfway state is kept until released: leave the camera time to focus.
                await Task.Delay(Math.Max(0, _options.AutoFocusHoldMilliseconds));
            }
            finally
            {
                await ReleaseShutterButtonAsync();
            }
        }
        finally
        {
            _shutterLock.Release();
        }
    }

    /// <summary>
    /// Releases the shutter button, retrying while the camera is busy. Never throws: a failure is logged.
    /// </summary>
    private async Task ReleaseShutterButtonAsync()
    {
        var attempts = Math.Max(5, _options.BusyRetryCount);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await RunAsync(camera =>
                    TraceCall("Release shutter button", () => EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF))
                        .ThrowIfEdSdkError("Could not release the shutter button"));
                return;
            }
            catch (EdsException e) when (e.IsBusy && attempt < attempts)
            {
                await Task.Delay(_options.BusyRetryDelayMilliseconds);
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Could not release the shutter button");
                return;
            }
        }
    }

    /// <summary>
    /// Takes a picture with the camera, optionally using autofocus.
    /// </summary>
    /// <returns>The bytes of the captured file.</returns>
    public async Task<byte[]?> TakePicture(bool useAutoFocus = true) => (await TakePictureAsync(useAutoFocus)).Data;

    /// <summary>
    /// Takes a picture and returns the first downloaded file matching <paramref name="fileTypes"/>
    /// (default: <see cref="CanonCameraOptions.CaptureFileTypes"/>).
    /// </summary>
    /// <exception cref="TimeoutException">The camera did not deliver a matching file in time.</exception>
    /// <exception cref="CaptureFailedException">The camera reported a capture failure.</exception>
    public async Task<CapturedImage> TakePictureAsync(bool useAutoFocus = true, IEnumerable<string>? fileTypes = null, CancellationToken cancellationToken = default)
    {
        var acceptedTypes = CaptureFileTypes.Normalize(fileTypes);
        if (acceptedTypes.Count == 0)
            acceptedTypes = _defaultFileTypes;

        var clock = Stopwatch.StartNew();
        _logger?.LogDebug("Capture requested (auto focus: {AutoFocus}, file types: {FileTypes})", useAutoFocus, string.Join(", ", acceptedTypes));

        await ConnectAsync();
        await _shutterLock.WaitAsync(cancellationToken);

        try
        {
            await WaitForPendingTransfersAsync(cancellationToken);

            // The setting may have been changed on the camera, and the SDK does not report it: set it again.
            if (_options.ForceFlashFiring)
                await RunAsync(camera => TrySetFlashFiring(camera, true));

            var timeout = await GetCaptureTimeout();
            var pending = new PendingCapture(acceptedTypes);
            _pendingCapture = pending;
            _capturing = true;
            var pressed = false;

            try
            {
                var button = useAutoFocus
                    ? EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely
                    : EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely_NonAF;

                // Press then release the shutter button (EDSDK API reference, sample 9).
                // Only the press is retried while the camera is busy: once it succeeded it is never sent again,
                // and the button is always released, even when the press failed.
                try
                {
                    await RunWithBusyRetryAsync(camera =>
                        TraceCall($"Press shutter button ({button})", () => EDSDK.EdsSendCommand(camera, EDSDK.CameraCommand_PressShutterButton, (int)button))
                            .ThrowIfEdSdkError("Could not press the shutter button"), cancellationToken);
                    pressed = true;
                }
                finally
                {
                    await ReleaseShutterButtonAsync();
                }

                _logger?.LogDebug("Waiting up to {Timeout} s for the file", timeout.TotalSeconds);
                var image = await pending.Completion.Task.WaitAsync(timeout, cancellationToken);
                _logger?.LogDebug("Capture completed in {Duration} ms: {File}", clock.ElapsedMilliseconds, image.FileName);
                return image;
            }
            catch (TimeoutException)
            {
                _logger?.LogDebug("Capture timed out after {Duration} ms", clock.ElapsedMilliseconds);
                _staleTransfers = true;
                throw new TimeoutException(
                    $"The camera did not deliver a picture within {timeout.TotalSeconds:0.#} s. " +
                    $"Check the focus and that the image quality set on the camera produces one of these file types: {string.Join(", ", acceptedTypes)}.");
            }
            catch (OperationCanceledException) when (pressed)
            {
                // The shot was taken: its file may still arrive and must not answer the next capture.
                _staleTransfers = true;
                throw;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger?.LogDebug("Capture failed after {Duration} ms: {Error}", clock.ElapsedMilliseconds, e.Message);
                throw;
            }
            finally
            {
                _pendingCapture = null;
                _capturing = false;
            }
        }
        finally
        {
            _shutterLock.Release();
        }
    }

    /// <summary>
    /// Waits (a few seconds at most) until the camera has transferred the files of previous shots,
    /// so they cannot be taken for the file of the next capture.
    /// </summary>
    private async Task WaitForPendingTransfersAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);

        while ((_transferJobPending || _staleTransfers) && DateTime.UtcNow < deadline)
            await Task.Delay(50, cancellationToken);

        // The camera may not report its job status: do not block the next captures forever.
        _staleTransfers = false;
    }

    /// <summary>
    /// Capture timeout: <see cref="CanonCameraOptions.CaptureTimeoutSeconds"/> plus the exposure time of the current shutter speed.
    /// </summary>
    private async Task<TimeSpan> GetCaptureTimeout()
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.CaptureTimeoutSeconds));

        try
        {
            var tv = await RunAsync(camera => EDSDK.EdsGetPropertyData(camera, EDSDK.PropID_Tv, 0, out uint value) == EDSDK.EDS_ERR_OK ? value : (uint?)null);

            if (tv.HasValue && EdsdkHelper.TryGetExposureSeconds(tv.Value, out var seconds))
                timeout += TimeSpan.FromSeconds(seconds);
        }
        catch (EdsException e) when (!e.IsDisconnected)
        {
            _logger?.LogDebug(e, "Could not read the shutter speed, using the default capture timeout");
        }

        return timeout;
    }

    #endregion

    #region Flash

    /// <summary>
    /// Creates the flash settings object and, if configured, sets flash firing to Fire. Runs on the Canon thread.
    /// </summary>
    private void InitializeFlash(nint camera)
    {
        var err = EDSDK.EdsCreateFlashSettingRef(camera, out var flashRef);
        if (err != EDSDK.EDS_ERR_OK || flashRef == nint.Zero)
        {
            _flashError = $"Flash settings not available: {EdsdkHelper.GetErrorMessage(err)}";
            _logger?.LogInformation("{Error}", _flashError);
            return;
        }

        _flashRef = flashRef;
        _flashError = null;

        if (_options.ForceFlashFiring)
            TrySetFlashFiring(camera, true);
    }

    /// <summary>
    /// Sets flash firing without throwing; a failure is logged and reported in <see cref="GetFlashStatus"/>.
    /// </summary>
    private void TrySetFlashFiring(nint camera, bool firing)
    {
        try
        {
            SetFlashFiringCore(camera, firing);
        }
        catch (EdsException e)
        {
            _logger?.LogWarning("Could not set flash firing to {Firing}: {Message}", firing ? "Fire" : "Off", e.Message);
        }
    }

    /// <summary>
    /// Sets kEdsPropID_Flash_Target then kEdsPropID_Flash_Firing (EDSDK API reference 6.32).
    /// The camera UI must be locked while the flash properties are set.
    /// </summary>
    private void SetFlashFiringCore(nint camera, bool firing)
    {
        try
        {
            if (_flashRef == nint.Zero)
                throw new EdsException(EDSDK.EDS_ERR_NOT_SUPPORTED, _flashError ?? "Flash settings not available");

            var target = string.Equals(_options.FlashTarget, "External", StringComparison.OrdinalIgnoreCase) ? 1u : 0u;

            TraceCall("UI lock", () => EDSDK.EdsSendStatusCommand(camera, EDSDK.CameraState_UILock, 1)).ThrowIfEdSdkError("Could not lock the camera UI");

            try
            {
                TraceCall("Flash target", () => EDSDK.EdsSetPropertyData(_flashRef, EDSDK.PropID_Flash_Target, 0, sizeof(uint), target))
                    .ThrowIfEdSdkError("Could not set the flash target");
                TraceCall("Flash firing", () => EDSDK.EdsSetPropertyData(_flashRef, EDSDK.PropID_Flash_Firing, 0, sizeof(uint), firing ? 1u : 0u))
                    .ThrowIfEdSdkError("Could not set flash firing (the camera must be in P, Tv, Av or M)");
            }
            finally
            {
                var unlock = TraceCall("UI unlock", () => EDSDK.EdsSendStatusCommand(camera, EDSDK.CameraState_UIUnLock, 0));
                if (unlock != EDSDK.EDS_ERR_OK)
                    _logger?.LogWarning("Could not unlock the camera UI: {Result}", EdsdkHelper.DescribeResult(unlock));
            }

            _flashFiring = firing;
            _flashError = null;
        }
        catch (EdsException e)
        {
            _flashError = e.Message;
            throw;
        }
    }

    /// <summary>
    /// Gets the state of the "flash firing" setting, as last set by the API.
    /// </summary>
    public Task<FlashStatus> GetFlashStatus() => RunAsync(_ =>
    {
        var firing = _flashFiring;

        // The SDK only returns a value once it has been set remotely (EDSDK API reference 6.32.2).
        if (firing.HasValue && _flashRef != nint.Zero
            && EDSDK.EdsGetPropertyData(_flashRef, EDSDK.PropID_Flash_Firing, 0, out uint value) == EDSDK.EDS_ERR_OK)
            firing = value != 0;

        return new FlashStatus(_flashRef != nint.Zero, firing, _options.ForceFlashFiring, _flashError);
    });

    /// <summary>
    /// Sets the "flash firing" camera setting (Fire or Off).
    /// With <see cref="CanonCameraOptions.ForceFlashFiring"/>, it is set back to Fire before the next capture.
    /// Waits for a capture or an autofocus in progress to finish.
    /// </summary>
    public async Task SetFlashFiringAsync(bool firing, CancellationToken cancellationToken = default)
    {
        await _shutterLock.WaitAsync(cancellationToken);

        try
        {
            await RunWithBusyRetryAsync(camera => SetFlashFiringCore(camera, firing), cancellationToken);
        }
        finally
        {
            _shutterLock.Release();
        }
    }

    #endregion

    #region Live view

    /// <summary>
    /// Starts streaming the live view to the PC (EDSDK API reference, sample 10).
    /// </summary>
    public Task StartLiveViewAsync()
    {
        _liveViewRequested = true;

        return RunWithBusyRetryAsync(camera =>
        {
            if (_liveViewActive)
                return;

            // Live view cannot start when it is disabled in the camera settings.
            if (EDSDK.EdsGetPropertyData(camera, EDSDK.PropID_Evf_Mode, 0, out uint evfMode) == EDSDK.EDS_ERR_OK && evfMode == 0)
                EDSDK.EdsSetPropertyData(camera, EDSDK.PropID_Evf_Mode, 0, sizeof(uint), 1u).ThrowIfEdSdkError("Could not enable live view");

            EDSDK.EdsGetPropertyData(camera, EDSDK.PropID_Evf_OutputDevice, 0, out uint device);

            var pcOutput = _options.LiveViewSmallImage ? EDSDK.EvfOutputDevice_PC_Small : EDSDK.EvfOutputDevice_PC;
            device = _options.KeepCameraScreenOn ? device | pcOutput : pcOutput;

            EDSDK.EdsSetPropertyData(camera, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), device).ThrowIfEdSdkError("Could not start live view");
            _liveViewActive = true;
            _logger?.LogInformation("Live view started");
        });
    }

    /// <summary>
    /// Stops streaming the live view to the PC, which gives the camera back its screen and saves power.
    /// </summary>
    public async Task StopLiveViewAsync()
    {
        _liveViewRequested = false;

        if (!_connected)
            return;

        await RunWithBusyRetryAsync(camera =>
        {
            if (!_liveViewActive)
                return;

            StopLiveViewCore(camera);
        });
    }

    private void StopLiveViewCore(nint camera)
    {
        if (EDSDK.EdsGetPropertyData(camera, EDSDK.PropID_Evf_OutputDevice, 0, out uint device) == EDSDK.EDS_ERR_OK)
        {
            device &= ~(EDSDK.EvfOutputDevice_PC | EDSDK.EvfOutputDevice_PC_Small);
            EDSDK.EdsSetPropertyData(camera, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), device).ThrowIfEdSdkError("Could not stop live view");
        }

        _liveViewActive = false;
        _logger?.LogInformation("Live view stopped");
    }

    /// <summary>
    /// Gets the current live view image (JPEG), starting the live view if needed.
    /// Returns null when no frame is available yet, or while a picture is being taken.
    /// </summary>
    public async Task<byte[]?> GetLiveView()
    {
        if (_capturing)
            return null;

        if (!_liveViewActive)
            await StartLiveViewAsync();

        return await RunAsync(camera =>
        {
            if (_capturing)
                return null;

            var evfImage = nint.Zero;
            var stream = nint.Zero;

            try
            {
                EDSDK.EdsCreateMemoryStream(0, out stream).ThrowIfEdSdkError("Could not create memory stream for EVF image");
                EDSDK.EdsCreateEvfImageRef(stream, out evfImage).ThrowIfEdSdkError("Could not create EVF image reference");

                var err = EDSDK.EdsDownloadEvfImage(camera, evfImage);

                // No frame yet (live view starting) or camera busy: not an error, skip the frame.
                if (err is EDSDK.EDS_ERR_OBJECT_NOTREADY or EDSDK.EDS_ERR_DEVICE_BUSY or EDSDK.EDS_ERR_PTP_DEVICE_BUSY)
                    return null;

                err.ThrowIfEdSdkError("Could not download EVF image");

                var bytes = CopyStream(stream);
                return bytes.Length > 0 ? bytes : null;
            }
            finally
            {
                if (evfImage != nint.Zero) EDSDK.EdsRelease(evfImage);
                if (stream != nint.Zero) EDSDK.EdsRelease(stream);
            }
        });
    }

    #endregion

    /// <summary>
    /// Stops the live view, closes the session and terminates the SDK.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            var cleanup = _thread.InvokeAsync(() =>
            {
                if (_cameraRef != nint.Zero && _liveViewActive)
                {
                    try
                    {
                        StopLiveViewCore(_cameraRef);
                    }
                    catch (Exception e)
                    {
                        _logger?.LogWarning(e, "Could not stop live view");
                    }
                }

                DisconnectCore("application stopping");

                if (_sdkInitialized)
                {
                    EDSDK.EdsTerminateSDK();
                    _sdkInitialized = false;
                }
            });

            if (!cleanup.Wait(TimeSpan.FromSeconds(5)))
                _logger?.LogWarning("Timed out while closing the camera session");
        }
        catch (Exception e)
        {
            _logger?.LogWarning(e, "Error while closing the camera session");
        }

        // _shutterLock is not disposed: an operation still in flight releases it in its finally block.
        _thread.Dispose();
    }
}
