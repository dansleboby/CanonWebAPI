namespace Canon.Core;

public enum CameraProperty: uint
{
    ISOSpeed = EDSDK.PropID_ISOSpeed,
    Aperture = EDSDK.PropID_Av,
    ShutterSpeed = EDSDK.PropID_Tv,
    ExposureCompensation = EDSDK.PropID_ExposureCompensation,
    WhiteBalance = EDSDK.PropID_WhiteBalance,
}