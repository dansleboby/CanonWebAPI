namespace Canon.Core;

/// <summary>
/// Enumeration of camera properties that can be controlled via the Canon SDK.
/// </summary>
public enum CameraProperty: uint
{
    ISOSpeed = EDSDK.PropID_ISOSpeed,
    Aperture = EDSDK.PropID_Av,
    ShutterSpeed = EDSDK.PropID_Tv,
    ExposureCompensation = EDSDK.PropID_ExposureCompensation,
    WhiteBalance = EDSDK.PropID_WhiteBalance,
}