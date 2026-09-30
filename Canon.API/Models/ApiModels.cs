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
/// <param name="Error">Why the camera state could not be read, if it could not.</param>
public sealed record CameraStatusResponse(
    bool Connected,
    string? CameraName,
    bool LiveViewActive,
    int LiveViewClients,
    CameraMode? Mode,
    TemperatureStatus? Temperature,
    string? Error);
