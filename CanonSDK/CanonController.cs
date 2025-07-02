using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using EDSDKLib;
using System.Runtime.InteropServices;
using System.Threading;

namespace CanonSDK
{
    /// <summary>
    /// Provides a controller class to interact with a connected Canon camera using the EDSDK,
    /// including a simple web server to provide URLs for downloaded images.
    /// </summary>
    public class CanonController : IDisposable
    {
        private IntPtr _camera = IntPtr.Zero;
        private bool _isLiveViewOn;
        private readonly object _lock = new object();
        private EDSDK.EdsObjectEventHandler _objectEventHandler; // Keep a reference to prevent garbage collection

        // --- Web Server Fields ---
        private HttpListener _httpListener;
        private string _lastDownloadedFilePath;
        private readonly string _serverUrl = "http://localhost:8080/";

        // --- Synchronization for synchronous download and event handling ---
        private readonly ManualResetEvent _downloadCompleteEvent = new ManualResetEvent(false);
        private Thread _eventPollingThread;
        private bool _runEventPolling;

        #region P/Invoke for COM Initialization
        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern int CoUninitialize();

        private const uint COINIT_APARTMENTTHREADED = 0x2;
        #endregion

        /// <summary>
        /// Initializes the SDK, detects the first connected camera, opens a session, and starts background event polling.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if SDK initialization fails or no camera is found.</exception>
        public void Initialize()
        {
            CheckError(EDSDK.EdsInitializeSDK(), "Could not initialize SDK.");

            CheckError(EDSDK.EdsGetCameraList(out var cameraList), "Could not get camera list.");

            CheckError(EDSDK.EdsGetChildCount(cameraList, out var count), "Could not get camera count.");
            
            if (count == 0)
                throw new InvalidOperationException("No Canon camera detected.");

            CheckError(EDSDK.EdsGetChildAtIndex(cameraList, 0, out _camera), "Could not get first camera.");
            EDSDK.EdsRelease(cameraList);

            // Start polling for camera events on a background thread
            StartEventPolling();
            // Start the local web server
            StartWebServer();
        }

        /// <summary>
        /// Handles camera object events, primarily for downloading newly created images automatically.
        /// </summary>
        private uint Camera_ObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            // Debug line to confirm the event handler is now being called.
            Console.WriteLine($"Camera event received: {inEvent:X8}, Ref: {inRef.ToString("X16")}");

            if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer)
            {
                Console.WriteLine("Image transfer request received from camera.");
            
                try
                {
                    DownloadImage(inRef);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error downloading image: {ex.Message}");
                }
            }

            if (inRef != IntPtr.Zero) 
                EDSDK.EdsRelease(inRef);

            return EDSDK.EDS_ERR_OK;
        }

        /// <summary>
        /// Downloads an image from the camera and stores its path for the web server.
        /// </summary>
        private void DownloadImage(IntPtr dirItemRef)
        {
            CheckError(EDSDK.EdsGetDirectoryItemInfo(dirItemRef, out var dirItemInfo), "Could not get directory item info.");

            var tempDir = Path.Combine(Path.GetTempPath(), "CanonSDKImages");
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, dirItemInfo.szFileName);

            var stream = IntPtr.Zero;
            try
            {
                CheckError(EDSDK.EdsCreateFileStream(filePath, EDSDK.EdsFileCreateDisposition.CreateAlways, EDSDK.EdsAccess.ReadWrite, out stream), "Could not create file stream.");
                CheckError(EDSDK.EdsDownload(dirItemRef, dirItemInfo.Size, stream), "Could not download image.");
            }
            finally
            {
                CheckError(EDSDK.EdsDownloadComplete(dirItemRef), "Could not complete download operation on camera.");
                if (stream != IntPtr.Zero)
                {
                    EDSDK.EdsRelease(stream);
                }
            }

            // Store the path of the latest downloaded image and signal completion
            lock (_lock)
            {
                _lastDownloadedFilePath = filePath;
            }
            _downloadCompleteEvent.Set(); // Signal that the download is complete

            Console.WriteLine($"Image downloaded to {filePath}");
            Console.WriteLine($"Access it at: {GetLastDownloadedImageUrl()}");
        }

        /// <summary>
        /// Returns the URL to view the most recently downloaded picture.
        /// </summary>
        /// <returns>A string containing the URL, or null if no picture has been taken yet.</returns>
        public string GetLastDownloadedImageUrl()
        {
            return string.IsNullOrEmpty(_lastDownloadedFilePath) ? null : $"{_serverUrl}latest-image.jpg";
        }


        /// <summary>
        /// Gets the connected camera's device name.
        /// </summary>
        public string GetCameraName()
        {
            if (_camera == IntPtr.Zero) throw new InvalidOperationException("Camera not initialized.");
            CheckError(EDSDK.EdsGetDeviceInfo(_camera, out var info), "Could not get device info.");
            return info.szDeviceDescription;
        }

        /// <summary>
        /// Takes a picture and waits for the download to complete.
        /// </summary>
        /// <param name="useAutoFocus">Set to true to perform autofocus before capture (default). Set to false to capture without autofocus.</param>
        /// <returns>The local file path of the downloaded image.</returns>
        public string TakePicture(bool useAutoFocus = true)
        {
            if (_camera == IntPtr.Zero) throw new InvalidOperationException("Camera not initialized.");

            _downloadCompleteEvent.Reset(); // Prepare to wait for the download signal

            var wasLiveViewOn = _isLiveViewOn;
            
            if (wasLiveViewOn)
            {
                StopLiveView();
                Thread.Sleep(500);
            }

            try
            {
                if (useAutoFocus)
                {
                    RetryCommand(() => EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_TakePicture, 0), "Could not take picture with AF.");
                }
                else
                {
                    var command = EDSDK.CameraCommand_PressShutterButton;
                    var param = (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely_NonAF;

                    RetryCommand(() => EDSDK.EdsSendCommand(_camera, command, param), "Could not send Non-AF capture command.");
                    Thread.Sleep(100);
                    RetryCommand(() => EDSDK.EdsSendCommand(_camera, EDSDK.CameraCommand_PressShutterButton, (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF), "Could not send shutter release command.");
                }
            }
            finally
            {
                if (wasLiveViewOn)
                {
                    StartLiveView();
                }
            }

            // Wait for the Camera_ObjectEvent to signal that the download is complete
            var downloadCompleted = _downloadCompleteEvent.WaitOne(100000); // 10 second timeout

            if (!downloadCompleted)
            {
                throw new TimeoutException("Timed out waiting for image download to complete.");
            }

            return _lastDownloadedFilePath;
        }

        /// <summary>
        /// Starts the live view feed from the camera.
        /// </summary>
        public void StartLiveView()
        {
            if (_camera == IntPtr.Zero) throw new InvalidOperationException("Camera not initialized.");
            if (_isLiveViewOn) return;

            var device = EDSDK.EvfOutputDevice_PC;
            object deviceObj = device;
            CheckError(EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), deviceObj), "Could not enable live view.");
            _isLiveViewOn = true;
        }

        /// <summary>
        /// Stops the live view feed.
        /// </summary>
        public void StopLiveView()
        {
            if (_camera == IntPtr.Zero || !_isLiveViewOn) return;

            uint device = 0;
            object deviceObj = device;
            CheckError(EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), deviceObj), "Could not disable live view.");
            _isLiveViewOn = false;
        }

        /// <summary>
        /// Gets a single frame from the live view feed as a byte array (JPEG).
        /// </summary>
        /// <returns>A byte array containing the JPEG image data for the live view frame.</returns>
        public byte[] GetLiveView()
        {
            lock (_lock)
            {
                if (_camera == IntPtr.Zero) throw new InvalidOperationException("Camera not initialized.");
                if (!_isLiveViewOn) StartLiveView();

                IntPtr evfImage = IntPtr.Zero, stream = IntPtr.Zero;
                try
                {
                    CheckError(EDSDK.EdsCreateMemoryStream(0, out stream), "Could not create memory stream.");
                    CheckError(EDSDK.EdsCreateEvfImageRef(stream, out evfImage), "Could not create EVF image ref.");
                    RetryCommand(() => EDSDK.EdsDownloadEvfImage(_camera, evfImage), "Could not download EVF image.", EDSDK.EDS_ERR_OBJECT_NOTREADY);

                    CheckError(EDSDK.EdsGetPointer(stream, out var imagePtr), "Could not get stream pointer.");
                    CheckError(EDSDK.EdsGetLength(stream, out var length), "Could not get stream length.");

                    var image = new byte[length];
                    Marshal.Copy(imagePtr, image, 0, (int)length);
                    return image;
                }
                finally
                {
                    if (stream != IntPtr.Zero) EDSDK.EdsRelease(stream);
                    if (evfImage != IntPtr.Zero) EDSDK.EdsRelease(evfImage);
                }
            }
        }

        /// <summary>
        /// Sets the camera's autofocus mode.
        /// </summary>
        /// <param name="mode">The AF mode to set (0: One-Shot, 1: AI Servo, 3: Manual).</param>
        public void SetAFMode(uint mode)
        {
            SetSetting(EDSDK.PropID_AFMode, mode);
        }

        /// <summary>
        /// Gets the current value and a list of available values for a given property.
        /// </summary>
        public uint GetSetting(uint propId, out Dictionary<uint, string> list)
        {
            if (_camera == IntPtr.Zero) throw new InvalidOperationException("Camera not initialized.");
            CheckError(EDSDK.EdsGetPropertyData(_camera, propId, 0, out uint value), $"Could not get property 0x{propId:X}");
            CheckError(EDSDK.EdsGetPropertyDesc(_camera, propId, out var desc), $"Could not get property description for 0x{propId:X}");
            var propertyMap = GetPropertyMap(propId);
            list = new Dictionary<uint, string>();
            for (var i = 0; i < desc.NumElements; i++)
            {
                var val = (uint)desc.PropDesc[i];
                list[val] = propertyMap.ContainsKey(val) ? propertyMap[val] : "Unknown";
            }
            return value;
        }

        /// <summary>
        /// Sets a camera property to a new value.
        /// </summary>
        public void SetSetting(uint propId, uint value)
        {
            if (_camera == IntPtr.Zero) throw new InvalidOperationException("Camera not initialized.");
            object val = value;
            RetryCommand(() => EDSDK.EdsSetPropertyData(_camera, propId, 0, sizeof(uint), val), $"Could not set property 0x{propId:X}.");
            Console.WriteLine($"Set property 0x{propId:X} to {value}");
        }

        /// <summary>
        /// Releases all camera resources, stops the web server and event polling, and terminates the SDK.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                StopEventPolling();
                _httpListener?.Stop();
            
                if (_camera != IntPtr.Zero)
                {
                    StopLiveView();
                    EDSDK.EdsCloseSession(_camera);
                    EDSDK.EdsRelease(_camera);
                    _camera = IntPtr.Zero;
                }
                
                EDSDK.EdsTerminateSDK();
            }
        }

        #region Helpers

        /// <summary>
        /// Starts the background thread that polls for camera events. This thread is
        /// initialized as a Single-Threaded Apartment (STA) as required by the EDSDK for event handling.
        /// </summary>
        private void StartEventPolling()
        {
            _runEventPolling = true;
            _eventPollingThread = new Thread(() =>
            {
                // Initialize COM for this thread. This is critical for event handling.
                CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);

                CheckError(EDSDK.EdsOpenSession(_camera), "Could not open camera session.");

                CheckError(EDSDK.EdsSetPropertyData(_camera, EDSDK.PropID_SaveTo, 0, sizeof(uint), (uint)EDSDK.EdsSaveTo.Host), "Could not set SaveTo host.");

                var capacity = new EDSDK.EdsCapacity { NumberOfFreeClusters = 0x7FFFFFFF, BytesPerSector = 0x1000, Reset = 1 };
                CheckError(EDSDK.EdsSetCapacity(_camera, capacity), "Could not set host capacity.");

                _objectEventHandler = Camera_ObjectEvent;
                CheckError(EDSDK.EdsSetObjectEventHandler(_camera, EDSDK.ObjectEvent_All, _objectEventHandler, IntPtr.Zero), "Could not set object event handler.");

                while (_runEventPolling)
                {
                    // EdsGetEvent is a blocking call that waits for the next event.
                    // This is the recommended way to handle events in a non-UI thread.
                    EDSDK.EdsGetEvent();
                    Thread.Sleep(100); // Small delay to prevent tight loop
                }

                // Uninitialize COM when the thread exits.
                CoUninitialize();
            })
            {
                IsBackground = true
            };

            _eventPollingThread.SetApartmentState(ApartmentState.STA);
            _eventPollingThread.Start();
            Console.WriteLine("Camera event polling started.");
        }

        /// <summary>
        /// Stops the event polling thread.
        /// </summary>
        private void StopEventPolling()
        {
            _runEventPolling = false;
            _eventPollingThread?.Join(500); // Wait briefly for the thread to finish
        }

        /// <summary>
        /// Starts the local HTTP server on a background thread.
        /// </summary>
        private void StartWebServer()
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"{_serverUrl}latest-image.jpg/");
            _httpListener.Start();

            ThreadPool.QueueUserWorkItem(state =>
            {
                while (_httpListener.IsListening)
                {
                    try
                    {
                        var context = _httpListener.GetContext();
                        HandleHttpRequest(context);
                    }
                    catch (HttpListenerException)
                    {
                        break; // Listener was stopped.
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"HTTP server error: {ex.Message}");
                    }
                }
            });

            Console.WriteLine($"Image server listening on {_serverUrl}");
        }

        /// <summary>
        /// Handles an incoming HTTP request to serve the latest image.
        /// </summary>
        private void HandleHttpRequest(HttpListenerContext context)
        {
            var response = context.Response;
            string currentFilePath;

            lock (_lock)
            {
                currentFilePath = _lastDownloadedFilePath;
            }

            if (string.IsNullOrEmpty(currentFilePath) || !File.Exists(currentFilePath))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                var buffer = System.Text.Encoding.UTF8.GetBytes("No image available yet.");
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                try
                {
                    var buffer = File.ReadAllBytes(currentFilePath);
                    response.ContentType = "image/jpeg";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                catch (IOException ex)
                {
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    var buffer = System.Text.Encoding.UTF8.GetBytes($"Error reading file: {ex.Message}");
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            response.OutputStream.Close();
        }

        /// <summary>
        /// Executes an EDSDK command with retry logic for busy and not-ready states.
        /// </summary>
        private void RetryCommand(Func<uint> command, string errorMessage, uint retryOnErrorCode = EDSDK.EDS_ERR_DEVICE_BUSY)
        {
            const int maxRetries = 10;
            const int delayMs = 100;
            for (var i = 0; i < maxRetries; i++)
            {
                var err = command();
                if (err == EDSDK.EDS_ERR_OK) return;
                if (err == EDSDK.EDS_ERR_DEVICE_BUSY || err == retryOnErrorCode)
                {
                    Console.WriteLine($"{errorMessage} Camera busy or not ready, retrying ({i + 1}/{maxRetries})...");
                    Thread.Sleep(delayMs);
                }
                else
                {
                    CheckError(err, errorMessage);
                }
            }
            throw new InvalidOperationException($"{errorMessage} Failed after {maxRetries} retries.");
        }

        /// <summary>
        /// Checks for an EDSDK error and throws a formatted exception if an error occurred.
        /// </summary>
        private void CheckError(uint error, string message)
        {
            if (error != EDSDK.EDS_ERR_OK)
            {
                throw new InvalidOperationException($"{message} EDSDK Error: 0x{error:X8}");
            }
        }

        // Helper map for converting uint property values to human-readable strings.
        private static readonly Dictionary<uint, string> AvValues = new Dictionary<uint, string>() { { 0x08, "1" }, { 0x0B, "1.1" }, { 0x0C, "1.2" }, { 0x0D, "1.2" }, { 0x10, "1.4" }, { 0x13, "1.6" }, { 0x14, "1.8" }, { 0x15, "1.8" }, { 0x18, "2" }, { 0x1B, "2.2" }, { 0x1C, "2.5" }, { 0x1D, "2.5" }, { 0x20, "2.8" }, { 0x23, "3.2" }, { 0x24, "3.5" }, { 0x25, "3.5" }, { 0x28, "4" }, { 0x2B, "4.5" }, { 0x2C, "4.5" }, { 0x2D, "5.0" }, { 0x30, "5.6" }, { 0x33, "6.3" }, { 0x34, "6.7" }, { 0x35, "7.1" }, { 0x38, "8" }, { 0x3B, "9" }, { 0x3C, "9.5" }, { 0x3D, "10" }, { 0x40, "11" }, { 0x43, "13" }, { 0x44, "13" }, { 0x45, "14" }, { 0x48, "16" }, { 0x4B, "18" }, { 0x4C, "19" }, { 0x4D, "20" }, { 0x50, "22" }, { 0x53, "25" }, { 0x54, "27" }, { 0x55, "29" }, { 0x58, "32" }, { 0x5B, "36" }, { 0x5C, "38" }, { 0x5D, "40" }, { 0x60, "45" }, { 0x63, "51" }, { 0x64, "54" }, { 0x65, "57" }, { 0x68, "64" }, { 0x6B, "72" }, { 0x6C, "76" }, { 0x6D, "80" }, { 0x70, "91" } };
        private static readonly Dictionary<uint, string> TvValues = new Dictionary<uint, string>() { { 0x0C, "BULB" }, { 0x10, "30\"" }, { 0x13, "25\"" }, { 0x14, "20\"" }, { 0x15, "20\"" }, { 0x18, "15\"" }, { 0x1B, "13\"" }, { 0x1C, "10\"" }, { 0x1D, "10\"" }, { 0x20, "8\"" }, { 0x23, "6\"" }, { 0x24, "6\"" }, { 0x25, "5\"" }, { 0x28, "4\"" }, { 0x2B, "3.2\"" }, { 0x2C, "3\"" }, { 0x2D, "2.5\"" }, { 0x30, "2\"" }, { 0x33, "1.6\"" }, { 0x34, "1.5\"" }, { 0x35, "1.3\"" }, { 0x38, "1\"" }, { 0x3B, "0.8\"" }, { 0x3C, "0.7\"" }, { 0x3D, "0.6\"" }, { 0x40, "0.5\"" }, { 0x43, "0.4\"" }, { 0x44, "0.3\"" }, { 0x45, "0.3\"" }, { 0x48, "1/4" }, { 0x4B, "1/5" }, { 0x4C, "1/6" }, { 0x4D, "1/6" }, { 0x50, "1/8" }, { 0x53, "1/10" }, { 0x54, "1/10" }, { 0x55, "1/13" }, { 0x58, "1/15" }, { 0x5B, "1/20" }, { 0x5C, "1/20" }, { 0x5D, "1/25" }, { 0x60, "1/30" }, { 0x63, "1/40" }, { 0x64, "1/45" }, { 0x65, "1/50" }, { 0x68, "1/60" }, { 0x6B, "1/80" }, { 0x6C, "1/90" }, { 0x6D, "1/100" }, { 0x70, "1/125" }, { 0x73, "1/160" }, { 0x74, "1/180" }, { 0x75, "1/200" }, { 0x78, "1/250" }, { 0x7B, "1/320" }, { 0x7C, "1/350" }, { 0x7D, "1/400" }, { 0x80, "1/500" }, { 0x83, "1/640" }, { 0x84, "1/750" }, { 0x85, "1/800" }, { 0x88, "1/1000" }, { 0x8B, "1/1250" }, { 0x8C, "1/1500" }, { 0x8D, "1/1600" }, { 0x90, "1/2000" }, { 0x93, "1/2500" }, { 0x94, "1/3000" }, { 0x95, "1/3200" }, { 0x98, "1/4000" }, { 0x9B, "1/5000" }, { 0x9C, "1/6000" }, { 0x9D, "1/6400" }, { 0xA0, "1/8000" } };
        private static readonly Dictionary<uint, string> ISOValues = new Dictionary<uint, string>() { { 0x00, "Auto" }, { 0x28, "6" }, { 0x30, "12" }, { 0x38, "25" }, { 0x40, "50" }, { 0x48, "100" }, { 0x4B, "125" }, { 0x4D, "160" }, { 0x50, "200" }, { 0x53, "250" }, { 0x55, "320" }, { 0x58, "400" }, { 0x5B, "500" }, { 0x5D, "640" }, { 0x60, "800" }, { 0x63, "1000" }, { 0x65, "1250" }, { 0x68, "1600" }, { 0x6B, "2000" }, { 0x6D, "2500" }, { 0x70, "3200" }, { 0x73, "4000" }, { 0x75, "5000" }, { 0x78, "6400" }, { 0x7B, "8000" }, { 0x7D, "10000" }, { 0x80, "12800" }, { 0x83, "16000" }, { 0x85, "20000" }, { 0x88, "25600" }, { 0x8B, "32000" }, { 0x8D, "40000" }, { 0x90, "51200" }, { 0x98, "102400" } };
        private static readonly Dictionary<uint, string> WhiteBalanceValues = new Dictionary<uint, string>() { { 0, "Auto" }, { 1, "Daylight" }, { 2, "Cloudy" }, { 3, "Tungsten" }, { 4, "Fluorescent" }, { 5, "Flash" }, { 6, "Manual" }, { 8, "Shade" }, { 9, "Color Temp" }, { 10, "Custom" }, { 11, "Custom 2" }, { 12, "Custom 3" }, { 15, "Manual 2" }, { 16, "Manual 3" } };

        private Dictionary<uint, string> GetPropertyMap(uint propId)
        {
            switch (propId)
            {
                case EDSDK.PropID_Av: return AvValues;
                case EDSDK.PropID_Tv: return TvValues;
                case EDSDK.PropID_ISOSpeed: return ISOValues;
                case EDSDK.PropID_WhiteBalance: return WhiteBalanceValues;
                default: return new Dictionary<uint, string>();
            }
        }
        #endregion
    }
}
