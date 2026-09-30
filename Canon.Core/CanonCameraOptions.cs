namespace Canon.Core;

/// <summary>
/// Settings of <see cref="CanonCamera"/>, bound to the "Canon" section of appsettings.json.
/// </summary>
public sealed class CanonCameraOptions
{
    public const string SectionName = "Canon";

    /// <summary>
    /// File types downloaded after a capture, by extension without the dot. The first matching file the camera sends
    /// answers the capture. Empty (default): JPEG only. Other files (RAW, HEIF...) are cancelled so the camera releases them.
    /// "jpg" also matches ".jpeg", "heif" also matches ".hif", "*" accepts every file.
    /// </summary>
    /// <remarks>
    /// Empty rather than ["jpg"] by default: the configuration binder appends to an existing array, so a configured
    /// ["cr3"] would bind as ["jpg", "cr3"].
    /// </remarks>
    public string[] CaptureFileTypes { get; set; } = [];

    /// <summary>
    /// Time allowed for the camera to deliver a capture, in seconds, on top of the exposure time.
    /// The exposure time is read from the current shutter speed, so long exposures do not time out.
    /// </summary>
    public double CaptureTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How long the shutter button is held halfway by the autofocus command, in milliseconds.
    /// </summary>
    public int AutoFocusHoldMilliseconds { get; set; } = 800;

    /// <summary>
    /// Requests the small live view image (kEdsEvfOutputDevice_PC_Small) instead of the normal one.
    /// Uses less bandwidth, at the cost of resolution. Not supported by every camera.
    /// </summary>
    public bool LiveViewSmallImage { get; set; }

    /// <summary>
    /// Keeps the camera screen output enabled while the live view is streamed to the PC.
    /// When false (default), the live view is sent to the PC only, which turns the camera screen off
    /// and locks the camera buttons (except SET) while the live view is active.
    /// </summary>
    public bool KeepCameraScreenOn { get; set; }

    /// <summary>
    /// Answers kEdsStateEvent_WillSoonShutDown with kEdsCameraCommand_ExtendShutDownTimer so the camera
    /// does not turn itself off while the application is running.
    /// </summary>
    public bool PreventAutoPowerOff { get; set; } = true;

    /// <summary>
    /// Sets the "flash firing" camera setting to Fire when the camera connects and before every capture,
    /// so a flash on the accessory shoe always fires even if the setting was changed on the camera.
    /// The camera must be in a creative zone mode (P, Tv, Av, M).
    /// </summary>
    public bool ForceFlashFiring { get; set; } = true;

    /// <summary>
    /// Target of the flash settings (kEdsPropID_Flash_Target): "Unspecified" (default, e.g. studio flash triggered
    /// by the center contact of the accessory shoe) or "External" (flash communicating with the camera).
    /// </summary>
    public string FlashTarget { get; set; } = "Unspecified";

    /// <summary>
    /// Number of attempts when the camera answers "device busy".
    /// </summary>
    public int BusyRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay between attempts when the camera is busy, in milliseconds. Canon recommends about 500 ms.
    /// </summary>
    public int BusyRetryDelayMilliseconds { get; set; } = 500;
}
