namespace Canon.Core;

/// <summary>
/// A file downloaded from the camera after a capture.
/// </summary>
public sealed record CapturedImage(byte[] Data, string FileName, string ContentType);

/// <summary>
/// The shooting mode the camera is in.
/// </summary>
/// <param name="AEModeCode">Raw kEdsPropID_AEMode value.</param>
/// <param name="AEMode">Label of the AE mode (e.g. "Manual Exposure", "Scene Intelligent Auto").</param>
/// <param name="IsCreativeZone">
/// True for P, Tv, Av, M, Bulb and Fv: exposure settings (ISO, aperture, shutter speed...) can be changed remotely.
/// In the other modes the camera chooses capture settings by itself.
/// </param>
/// <param name="IsMovieMode">True when the camera is in movie mode, null if the camera does not report it.</param>
public sealed record CameraMode(uint AEModeCode, string AEMode, bool IsCreativeZone, bool? IsMovieMode);

/// <summary>
/// State of the "flash firing" camera setting (kEdsPropID_Flash_Firing).
/// </summary>
/// <param name="IsSupported">False when the camera does not expose flash settings (EdsCreateFlashSettingRef failed).</param>
/// <param name="Firing">
/// True: Fire, false: Off, null: unknown. The SDK only reports values set remotely:
/// a change made in the camera menu is not visible until the value is set again by the API.
/// </param>
/// <param name="ForcedBeforeCapture">True when the setting is set to Fire before every capture (Canon:ForceFlashFiring).</param>
/// <param name="LastError">Why the last attempt to set the flash failed, if it did.</param>
public sealed record FlashStatus(bool IsSupported, bool? Firing, bool ForcedBeforeCapture, string? LastError)
{
    /// <summary>
    /// Parses "fire"/"on"/"true" and "off"/"false" (case-insensitive).
    /// </summary>
    public static bool TryParseFiring(string? text, out bool firing)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "fire" or "on" or "true" or "1" or "enable" or "enabled":
                firing = true;
                return true;
            case "off" or "false" or "0" or "disable" or "disabled":
                firing = false;
                return true;
            default:
                firing = false;
                return false;
        }
    }
}

/// <summary>
/// Restrictions applied by the camera because of its internal temperature (kEdsPropID_TempStatus).
/// </summary>
/// <param name="IsSupported">False when the camera does not report its temperature status.</param>
/// <param name="RawValue">Raw property value.</param>
/// <param name="Status">Still image status: Normal, Warning, ReducedFrameRate, LiveViewProhibited, ShootingProhibited, DegradedImageQuality.</param>
/// <param name="MovieStatus">Movie status: Normal or MovieRecordingRestricted.</param>
/// <param name="IsWarning">True when any restriction or warning is active.</param>
public sealed record TemperatureStatus(bool IsSupported, uint? RawValue, string Status, string MovieStatus, bool IsWarning)
{
    public static TemperatureStatus Unsupported { get; } = new(false, null, "Unknown", "Unknown", false);

    public static TemperatureStatus FromRawValue(uint value)
    {
        var status = (value & 0xFFFF) switch
        {
            0x0000 => "Normal",
            0x0001 => "Warning",
            0x0002 => "ReducedFrameRate",
            0x0003 => "LiveViewProhibited",
            0x0004 => "ShootingProhibited",
            0x0005 => "DegradedImageQuality",
            var other => $"Unknown (0x{other:X})"
        };

        var movieStatus = (value >> 16) switch
        {
            0x0000 => "Normal",
            0x0002 => "MovieRecordingRestricted",
            var other => $"Unknown (0x{other:X})"
        };

        return new TemperatureStatus(true, value, status, movieStatus, value != 0);
    }
}
