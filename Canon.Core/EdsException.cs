namespace Canon.Core;

/// <summary>
/// An error returned by the Canon EDSDK.
/// </summary>
public class EdsException(uint errorCode, string message, Exception? innerException = null)
    : Exception($"{message}: {EdsdkHelper.GetErrorMessage(errorCode)}", innerException)
{
    public uint ErrorCode { get; } = errorCode;

    /// <summary>
    /// The camera is temporarily busy; retrying later may succeed.
    /// </summary>
    public bool IsBusy => ErrorCode is EDSDK.EDS_ERR_DEVICE_BUSY or EDSDK.EDS_ERR_PTP_DEVICE_BUSY;

    /// <summary>
    /// The camera is not connected, was disconnected or its session is closed.
    /// </summary>
    public bool IsDisconnected => ErrorCode is EDSDK.EDS_ERR_DEVICE_NOT_FOUND
        or EDSDK.EDS_ERR_COMM_DISCONNECTED
        or EDSDK.EDS_ERR_SESSION_NOT_OPEN
        or EDSDK.EDS_ERR_COMM_USB_BUS_ERR
        or EDSDK.EDS_ERR_COMM_PORT_IS_IN_USE;

    /// <summary>
    /// The camera refused to take the picture (focus failure, no lens, movie mode...).
    /// </summary>
    public bool IsTakePictureError => ErrorCode is >= EDSDK.EDS_ERR_TAKE_PICTURE_AF_NG and <= EDSDK.EDS_ERR_TAKE_PICTURE_RETRUCTED_LENS_NG;

    /// <summary>
    /// The camera refused the operation in its current state (e.g. lens cover closed).
    /// </summary>
    public bool IsRefused => ErrorCode is EDSDK.EDS_ERR_OPERATION_REFUSED or EDSDK.EDS_ERR_LENS_COVER_CLOSE;

    /// <summary>
    /// The camera does not accept or support the requested property or value.
    /// </summary>
    public bool IsInvalidValue => ErrorCode is EDSDK.EDS_ERR_INVALID_PARAMETER
        or EDSDK.EDS_ERR_INVALID_DEVICEPROP_VALUE
        or EDSDK.EDS_ERR_INVALID_DEVICEPROP_FORMAT
        or EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE
        or EDSDK.EDS_ERR_DEVICEPROP_NOT_SUPPORTED
        or EDSDK.EDS_ERR_NOT_SUPPORTED;
}

/// <summary>
/// No camera is connected, or the camera was disconnected.
/// </summary>
public class CameraNotConnectedException(string message, Exception? innerException = null)
    : EdsException(EDSDK.EDS_ERR_DEVICE_NOT_FOUND, message, innerException);

/// <summary>
/// The camera reported that a remote release failed (kEdsStateEvent_CaptureError).
/// </summary>
public class CaptureFailedException(uint captureErrorCode)
    : Exception(EdsdkHelper.GetCaptureErrorMessage(captureErrorCode))
{
    /// <summary>
    /// The parameter of kEdsStateEvent_CaptureError (EDSDK API reference 4.2.17).
    /// </summary>
    public uint CaptureErrorCode { get; } = captureErrorCode;
}
