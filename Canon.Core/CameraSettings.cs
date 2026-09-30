namespace Canon.Core;

/// <summary>
/// Why a requested setting value is refused.
/// </summary>
public enum SettingErrorReason
{
    /// <summary>The label cannot be read (unknown label or malformed raw value).</summary>
    InvalidValue,

    /// <summary>The value is not one of the values the camera accepts now.</summary>
    ValueNotAccepted,

    /// <summary>The setting cannot be changed in the current shooting mode (e.g. shutter speed in Av).</summary>
    NotSettableInMode
}

/// <summary>
/// A requested setting value that was refused before anything was written to the camera.
/// </summary>
/// <param name="Property">The setting.</param>
/// <param name="RequestedValue">The value as requested by the client.</param>
/// <param name="Reason">Why it is refused.</param>
/// <param name="AcceptedValues">Labels of the values the camera accepts now (empty when unknown or not settable).</param>
/// <param name="Message">Human readable explanation.</param>
public sealed record SettingError(
    CameraProperty Property,
    string? RequestedValue,
    SettingErrorReason Reason,
    IReadOnlyList<string> AcceptedValues,
    string Message);

/// <summary>
/// Current value of a camera setting, the values the camera accepts now and whether it can be changed in the current mode.
/// </summary>
/// <param name="Value">Current value label (e.g. "1/125"), or a raw value ("0x93") when unknown.</param>
/// <param name="SupportedValues">Labels of the values the camera accepts now (empty when the camera does not list them).</param>
/// <param name="Settable">True when the setting can be changed in the current shooting mode.</param>
public sealed record CameraSettingState(string Value, IReadOnlyList<string> SupportedValues, bool Settable);

/// <summary>
/// The shooting mode and the exposure settings of the camera, read in one call.
/// </summary>
public sealed record CameraSettings(
    CameraMode Mode,
    CameraSettingState Iso,
    CameraSettingState Aperture,
    CameraSettingState ShutterSpeed,
    CameraSettingState ExposureCompensation,
    CameraSettingState WhiteBalance);

/// <summary>
/// Validation rules of the camera settings. Pure functions: no camera needed.
/// </summary>
public static class CameraSettingRules
{
    /// <summary>
    /// Order in which several settings are written.
    /// </summary>
    public static IReadOnlyList<CameraProperty> WriteOrder { get; } =
    [
        CameraProperty.ISOSpeed,
        CameraProperty.Aperture,
        CameraProperty.ShutterSpeed,
        CameraProperty.ExposureCompensation,
        CameraProperty.WhiteBalance
    ];

    private const uint ProgramAE = 0x00;
    private const uint ShutterPriority = 0x01;
    private const uint AperturePriority = 0x02;
    private const uint Manual = 0x03;
    private const uint Bulb = 0x04;
    private const uint AutoDepthOfField = 0x05;
    private const uint DepthOfField = 0x06;
    private const uint FlexiblePriority = 0x37;

    // kEdsPropID_Tv value of Bulb: it can never be set from a computer (EDSDK API reference, kEdsPropID_Tv).
    private const uint TvBulb = 0x0C;

    /// <summary>kEdsPropID_ISOSpeed value of ISO Auto.</summary>
    public const uint IsoAuto = 0x00;

    /// <summary>
    /// Settings that can be changed in each creative zone mode. In the other (basic zone) modes, the camera sets
    /// capture-related properties by itself and none can be changed (EDSDK API reference, kEdsPropID_AEModeSelect).
    /// EdsGetPropertyDesc does not say whether a setting is locked by the mode (its "form" and "access" fields are
    /// reserved, always 0, EDSDK 13.20 API reference 3.1.20), hence this table.
    /// Exposure compensation in manual exposure mode: see <see cref="IsSettableInMode"/>.
    /// </summary>
    private static readonly Dictionary<uint, CameraProperty[]> SettableByMode = new()
    {
        { ProgramAE, [CameraProperty.ISOSpeed, CameraProperty.ExposureCompensation, CameraProperty.WhiteBalance] },
        { ShutterPriority, [CameraProperty.ISOSpeed, CameraProperty.ShutterSpeed, CameraProperty.ExposureCompensation, CameraProperty.WhiteBalance] },
        { AperturePriority, [CameraProperty.ISOSpeed, CameraProperty.Aperture, CameraProperty.ExposureCompensation, CameraProperty.WhiteBalance] },
        { Manual, [CameraProperty.ISOSpeed, CameraProperty.Aperture, CameraProperty.ShutterSpeed, CameraProperty.WhiteBalance] },
        { Bulb, [CameraProperty.ISOSpeed, CameraProperty.Aperture, CameraProperty.WhiteBalance] },
        { AutoDepthOfField, [CameraProperty.ISOSpeed, CameraProperty.ExposureCompensation, CameraProperty.WhiteBalance] },
        { DepthOfField, [CameraProperty.ISOSpeed, CameraProperty.ExposureCompensation, CameraProperty.WhiteBalance] },
        { FlexiblePriority, [CameraProperty.ISOSpeed, CameraProperty.Aperture, CameraProperty.ShutterSpeed, CameraProperty.ExposureCompensation, CameraProperty.WhiteBalance] },
    };

    /// <summary>
    /// True when <paramref name="property"/> can be changed in the AE mode <paramref name="aeMode"/> (kEdsPropID_AEMode value).
    /// </summary>
    /// <param name="isoAuto">
    /// ISO is Auto. In manual exposure mode, exposure compensation only applies with ISO Auto (EOS R user guides);
    /// the EDSDK API reference (5.2.28) predates it and says it is never available in that mode.
    /// </param>
    public static bool IsSettableInMode(CameraProperty property, uint aeMode, bool isoAuto = false) =>
        (SettableByMode.TryGetValue(aeMode, out var properties) && properties.Contains(property))
        || (property == CameraProperty.ExposureCompensation && aeMode == Manual && isoAuto);

    /// <summary>
    /// True when <paramref name="property"/> can be changed now: allowed by the AE mode, and the camera lists at least
    /// one settable value. An empty list (EdsGetPropertyDesc succeeded with no element) means the setting cannot be
    /// changed in the current state (movie mode, no lens...): the Canon samples disable the setting in that case,
    /// and the EDSDK API reference asks to set only values from that list. Null means the camera does not list the values.
    /// </summary>
    public static bool IsSettable(CameraProperty property, uint aeMode, IReadOnlyCollection<uint>? settableValues, bool isoAuto = false) =>
        IsSettableInMode(property, aeMode, isoAuto) && settableValues is not { Count: 0 };

    /// <summary>
    /// Checks a requested value before it is written to the camera.
    /// </summary>
    /// <param name="property">The setting.</param>
    /// <param name="requested">The label (e.g. "1/125", "5.6", "Auto") or raw value ("0x93").</param>
    /// <param name="aeMode">Current kEdsPropID_AEMode value.</param>
    /// <param name="settableValues">
    /// Values the camera accepts now (EdsGetPropertyDesc). Null when the camera does not list them: only the label and
    /// the mode are checked. Empty: the setting cannot be changed now (see <see cref="IsSettable"/>).
    /// </param>
    /// <param name="value">The raw value to write, when valid.</param>
    /// <param name="isoAuto">ISO is Auto once the settings are applied (see <see cref="IsSettableInMode"/>).</param>
    /// <returns>Null when the value can be written, otherwise why it cannot.</returns>
    public static SettingError? Validate(CameraProperty property, string? requested, uint aeMode, IReadOnlyCollection<uint>? settableValues, out uint value, bool isoAuto = false)
    {
        var propId = (uint)property;
        var accepted = (IReadOnlyList<string>)(settableValues ?? []).Select(v => propId.DescribeValue(v)).ToList();

        if (string.IsNullOrWhiteSpace(requested) || !propId.TryParseValue(requested, out value))
        {
            value = 0;
            return new SettingError(property, requested, SettingErrorReason.InvalidValue, accepted,
                $"Invalid value '{requested}' for {property.ToSettingName()}.");
        }

        if (!IsSettableInMode(property, aeMode, isoAuto))
        {
            return new SettingError(property, requested, SettingErrorReason.NotSettableInMode, [],
                $"{property.ToSettingName()} cannot be changed in the current shooting mode ({DescribeAEMode(aeMode)})"
                + (property == CameraProperty.ExposureCompensation && aeMode == Manual ? " unless ISO is Auto." : "."));
        }

        if (settableValues is { Count: 0 })
        {
            return new SettingError(property, requested, SettingErrorReason.NotSettableInMode, [],
                $"{property.ToSettingName()} cannot be changed now: the camera lists no settable value in its current state " +
                $"({DescribeAEMode(aeMode)}; movie mode, lens...).");
        }

        // A label shared by a 1/2 and a 1/3 stop value (e.g. aperture "2.5"): take the one allowed by the current exposure step.
        if (settableValues != null && !settableValues.Contains(value))
            value = propId.GetSameLabelValues(requested).FirstOrDefault(settableValues.Contains, value);

        if ((settableValues != null && !settableValues.Contains(value)) || (property == CameraProperty.ShutterSpeed && value == TvBulb))
        {
            return new SettingError(property, requested, SettingErrorReason.ValueNotAccepted, accepted,
                $"The camera does not accept {property.ToSettingName()} '{propId.DescribeValue(value)}' now."
                + (accepted.Count > 0 ? $" Accepted values: {string.Join(", ", accepted)}" : string.Empty));
        }

        return null;
    }

    /// <summary>
    /// Label of an AE mode (e.g. "Manual Exposure"), or its raw value when unknown.
    /// </summary>
    public static string DescribeAEMode(uint aeMode) =>
        EdsdkHelper.AEModeValues.TryGetValue(aeMode, out var label) ? label : EdsdkHelper.FormatRawValue(aeMode);

    /// <summary>
    /// Name of the setting in the API (e.g. "shutterSpeed").
    /// </summary>
    public static string ToSettingName(this CameraProperty property) => property switch
    {
        CameraProperty.ISOSpeed => "iso",
        CameraProperty.Aperture => "aperture",
        CameraProperty.ShutterSpeed => "shutterSpeed",
        CameraProperty.ExposureCompensation => "exposureCompensation",
        CameraProperty.WhiteBalance => "whiteBalance",
        _ => property.ToString()
    };

    /// <summary>
    /// Machine readable code of a reason (e.g. "not-settable-in-mode").
    /// </summary>
    public static string ToCode(this SettingErrorReason reason) => reason switch
    {
        SettingErrorReason.InvalidValue => "invalid-value",
        SettingErrorReason.ValueNotAccepted => "value-not-accepted",
        SettingErrorReason.NotSettableInMode => "not-settable-in-mode",
        _ => reason.ToString()
    };
}

/// <summary>
/// One or more requested settings were refused. Nothing was written to the camera.
/// </summary>
public class CameraSettingsException(CameraMode mode, IReadOnlyList<SettingError> errors)
    : Exception(string.Join(" ", errors.Select(e => e.Message)))
{
    /// <summary>The shooting mode when the settings were checked.</summary>
    public CameraMode Mode { get; } = mode;

    /// <summary>Every refused setting, in write order.</summary>
    public IReadOnlyList<SettingError> Errors { get; } = errors;

    /// <summary>
    /// The error that decides the response: a setting locked by the shooting mode comes first, since the mode must be
    /// changed on the camera before the values can be checked meaningfully.
    /// </summary>
    public SettingError PrimaryError => Errors.FirstOrDefault(e => e.Reason == SettingErrorReason.NotSettableInMode) ?? Errors[0];
}

/// <summary>
/// A setting could not be written after the validation passed. The settings in <see cref="Applied"/> were written;
/// <see cref="Failed"/> and the ones after it were not.
/// </summary>
public class CameraSettingsApplyException(IReadOnlyList<CameraProperty> applied, CameraProperty failed, string failedValue, Exception innerException)
    : Exception(
        $"Could not set {failed.ToSettingName()} to '{failedValue}'"
        + (applied.Count > 0 ? $" (already set: {string.Join(", ", applied.Select(p => p.ToSettingName()))})" : " (nothing was set)")
        + $": {innerException.Message}",
        innerException)
{
    public IReadOnlyList<CameraProperty> Applied { get; } = applied;
    public CameraProperty Failed { get; } = failed;
    public string FailedValue { get; } = failedValue;
}
