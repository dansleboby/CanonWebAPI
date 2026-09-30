using System.Text.Json.Serialization;
using Canon.Core;

namespace Canon.API.Models;

/// <summary>
/// Current value of a camera setting and the values the camera accepts now.
/// </summary>
public sealed record PropertyValueResponse(string Value, IReadOnlyList<string> SupportedValues);

/// <summary>
/// Overview of the camera state.
/// </summary>
/// <param name="Connected">True when a session is open with the camera.</param>
/// <param name="CameraName">Camera model, when connected.</param>
/// <param name="LiveViewActive">True while the camera streams its live view to the PC.</param>
/// <param name="LiveViewClients">Number of clients connected to /videostream.</param>
/// <param name="Mode">Shooting mode, when connected.</param>
/// <param name="Temperature">Temperature restrictions, when connected.</param>
/// <param name="Flash">State of the "flash firing" setting, when connected.</param>
/// <param name="Error">Why the camera state could not be read, if it could not.</param>
public sealed record CameraStatusResponse(
    bool Connected,
    string? CameraName,
    bool LiveViewActive,
    int LiveViewClients,
    CameraMode? Mode,
    TemperatureStatus? Temperature,
    FlashStatus? Flash,
    string? Error);

/// <summary>
/// Settings to apply with POST /settings. Omitted (or null) settings are left unchanged; an unknown field is refused,
/// so a misspelled setting is not silently ignored.
/// </summary>
/// <param name="Iso">ISO speed (e.g. "Auto", "400").</param>
/// <param name="Aperture">Aperture (e.g. "5.6").</param>
/// <param name="ShutterSpeed">Shutter speed (e.g. "1/125").</param>
/// <param name="ExposureCompensation">Exposure compensation (e.g. "0", "+1/3", "-1 2/3").</param>
/// <param name="WhiteBalance">White balance (e.g. "Auto", "Daylight").</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CameraSettingsRequest(
    string? Iso = null,
    string? Aperture = null,
    string? ShutterSpeed = null,
    string? ExposureCompensation = null,
    string? WhiteBalance = null)
{
    public Dictionary<CameraProperty, string> ToDictionary()
    {
        var settings = new Dictionary<CameraProperty, string>();
        if (Iso != null) settings[CameraProperty.ISOSpeed] = Iso;
        if (Aperture != null) settings[CameraProperty.Aperture] = Aperture;
        if (ShutterSpeed != null) settings[CameraProperty.ShutterSpeed] = ShutterSpeed;
        if (ExposureCompensation != null) settings[CameraProperty.ExposureCompensation] = ExposureCompensation;
        if (WhiteBalance != null) settings[CameraProperty.WhiteBalance] = WhiteBalance;
        return settings;
    }
}
