using System.Reflection;

namespace Canon.Core;

internal static class EdsdkHelper
{
    public static void ThrowIfEdSdkError(this uint error, string message)
    {
        if (error != EDSDK.EDS_ERR_OK)
            throw new EdsException(error, message, null);
    }

    internal static readonly Dictionary<uint, string> ErrorMessages = new()
    {
        // General errors
        { EDSDK.EDS_ERR_UNIMPLEMENTED, "Not implemented" },
        { EDSDK.EDS_ERR_INTERNAL_ERROR, "Internal error" },
        { EDSDK.EDS_ERR_MEM_ALLOC_FAILED, "Memory allocation error" },
        { EDSDK.EDS_ERR_MEM_FREE_FAILED, "Memory release error" },
        { EDSDK.EDS_ERR_OPERATION_CANCELLED, "Operation canceled" },
        { EDSDK.EDS_ERR_INCOMPATIBLE_VERSION, "Version error" },
        { EDSDK.EDS_ERR_NOT_SUPPORTED, "Not supported" },
        { EDSDK.EDS_ERR_UNEXPECTED_EXCEPTION, "Unexpected exception" },
        { EDSDK.EDS_ERR_PROTECTION_VIOLATION, "Protection violation" }, 
        { EDSDK.EDS_ERR_MISSING_SUBCOMPONENT, "Missing sub-component" },
        { EDSDK.EDS_ERR_SELECTION_UNAVAILABLE, "Selection unavailable" },

        // File access errors
        { EDSDK.EDS_ERR_FILE_IO_ERROR, "IO error" },
        { EDSDK.EDS_ERR_FILE_TOO_MANY_OPEN, "Too many files open" },
        { EDSDK.EDS_ERR_FILE_NOT_FOUND, "File does not exist" },
        { EDSDK.EDS_ERR_FILE_OPEN_ERROR, "Open error" },
        { EDSDK.EDS_ERR_FILE_CLOSE_ERROR, "Close error" },
        { EDSDK.EDS_ERR_FILE_SEEK_ERROR, "Seek error" },
        { EDSDK.EDS_ERR_FILE_TELL_ERROR, "Tell error" },
        { EDSDK.EDS_ERR_FILE_READ_ERROR, "Read error" },
        { EDSDK.EDS_ERR_FILE_WRITE_ERROR, "Write error" },
        { EDSDK.EDS_ERR_FILE_PERMISSION_ERROR, "Permission error" },
        { EDSDK.EDS_ERR_FILE_DISK_FULL_ERROR, "Disk full" },
        { EDSDK.EDS_ERR_FILE_ALREADY_EXISTS, "File already exists" },
        { EDSDK.EDS_ERR_FILE_FORMAT_UNRECOGNIZED, "Format error" },
        { EDSDK.EDS_ERR_FILE_DATA_CORRUPT, "Invalid data" },
        { EDSDK.EDS_ERR_FILE_NAMING_NA, "File naming error" },

        // Directory errors
        { EDSDK.EDS_ERR_DIR_NOT_FOUND, "Directory does not exist" },
        { EDSDK.EDS_ERR_DIR_IO_ERROR, "I/O error" },
        { EDSDK.EDS_ERR_DIR_ENTRY_NOT_FOUND, "No file in directory" },
        { EDSDK.EDS_ERR_DIR_ENTRY_EXISTS, "File in directory" },
        { EDSDK.EDS_ERR_DIR_NOT_EMPTY, "Directory full" },

        // Property errors
        { EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE, "Property (and additional property information) unavailable" },
        { EDSDK.EDS_ERR_PROPERTIES_MISMATCH, "Property mismatch" },
        { EDSDK.EDS_ERR_PROPERTIES_NOT_LOADED, "Property not loaded" },

        // Function parameter errors
        { EDSDK.EDS_ERR_INVALID_PARAMETER, "Invalid function parameter" },
        { EDSDK.EDS_ERR_INVALID_HANDLE, "Handle error" },
        { EDSDK.EDS_ERR_INVALID_POINTER, "Pointer error" },
        { EDSDK.EDS_ERR_INVALID_INDEX, "Index error" },
        { EDSDK.EDS_ERR_INVALID_LENGTH, "Length error" },
        { EDSDK.EDS_ERR_INVALID_FN_POINTER, "FN pointer error" },
        { EDSDK.EDS_ERR_INVALID_SORT_FN, "Sort FN error" },

        // Device errors
        { EDSDK.EDS_ERR_DEVICE_NOT_FOUND, "Device not found" },
        { EDSDK.EDS_ERR_DEVICE_BUSY, "Device is busy" },
        { EDSDK.EDS_ERR_DEVICE_INVALID, "Device error" },
        { EDSDK.EDS_ERR_DEVICE_EMERGENCY, "Device emergency" },
        { EDSDK.EDS_ERR_DEVICE_MEMORY_FULL, "Device memory full" },
        { EDSDK.EDS_ERR_DEVICE_INTERNAL_ERROR, "Internal device error" },
        { EDSDK.EDS_ERR_DEVICE_INVALID_PARAMETER, "Device parameter invalid" },
        { EDSDK.EDS_ERR_DEVICE_NO_DISK, "No disk" },
        { EDSDK.EDS_ERR_DEVICE_DISK_ERROR, "Disk error" },
        { EDSDK.EDS_ERR_DEVICE_CF_GATE_CHANGED, "The CF gate has been changed" },
        { EDSDK.EDS_ERR_DEVICE_DIAL_CHANGED, "The dial has been changed" },
        { EDSDK.EDS_ERR_DEVICE_NOT_INSTALLED, "Device not installed" },
        { EDSDK.EDS_ERR_DEVICE_STAY_AWAKE, "Device connected in awake mode" },
        { EDSDK.EDS_ERR_DEVICE_NOT_RELEASED, "Device not released" },

        // Stream errors
        { EDSDK.EDS_ERR_STREAM_IO_ERROR, "Stream I/O error" },
        { EDSDK.EDS_ERR_STREAM_NOT_OPEN, "Stream open error" },
        { EDSDK.EDS_ERR_STREAM_ALREADY_OPEN, "Stream already open" },
        { EDSDK.EDS_ERR_STREAM_OPEN_ERROR, "Failed to open stream" },
        { EDSDK.EDS_ERR_STREAM_CLOSE_ERROR, "Failed to close stream" },
        { EDSDK.EDS_ERR_STREAM_SEEK_ERROR, "Stream seek error" },
        { EDSDK.EDS_ERR_STREAM_TELL_ERROR, "Stream tell error" },
        { EDSDK.EDS_ERR_STREAM_READ_ERROR, "Failed to read stream" },
        { EDSDK.EDS_ERR_STREAM_WRITE_ERROR, "Failed to write stream" },
        { EDSDK.EDS_ERR_STREAM_PERMISSION_ERROR, "Permission error" },
        { EDSDK.EDS_ERR_STREAM_COULDNT_BEGIN_THREAD, "Could not start reading thumbnail" },
        { EDSDK.EDS_ERR_STREAM_BAD_OPTIONS, "Invalid stream option" },
        { EDSDK.EDS_ERR_STREAM_END_OF_STREAM, "Invalid stream termination" },

        // Communication errors
        { EDSDK.EDS_ERR_COMM_PORT_IS_IN_USE, "Port in use" },
        { EDSDK.EDS_ERR_COMM_DISCONNECTED, "Port disconnected" },
        { EDSDK.EDS_ERR_COMM_DEVICE_INCOMPATIBLE, "Incompatible device" },
        { EDSDK.EDS_ERR_COMM_BUFFER_FULL, "Buffer full" },
        { EDSDK.EDS_ERR_COMM_USB_BUS_ERR, "USB bus error" },

        // Camera lock ui errors
        { EDSDK.EDS_ERR_USB_DEVICE_LOCK_ERROR, "Failed to lock the UI" },
        { EDSDK.EDS_ERR_USB_DEVICE_UNLOCK_ERROR, "Failed to unlock the UI" },

        // STI - WIA errors
        { EDSDK.EDS_ERR_STI_UNKNOWN_ERROR, "Unknown STI" },
        { EDSDK.EDS_ERR_STI_INTERNAL_ERROR, "Internal STI error" },
        { EDSDK.EDS_ERR_STI_DEVICE_CREATE_ERROR, "Device creation error" },
        { EDSDK.EDS_ERR_STI_DEVICE_RELEASE_ERROR, "Device release error" },
        { EDSDK.EDS_ERR_DEVICE_NOT_LAUNCHED, "Device startup failed" },

        // Other general errors
        { EDSDK.EDS_ERR_ENUM_NA, "Enumeration terminated (there was no suitable enumeration item)" },
        { EDSDK.EDS_ERR_INVALID_FN_CALL, "Called in a mode when the function could not be used" },
        { EDSDK.EDS_ERR_HANDLE_NOT_FOUND, "Handle not found" },
        { EDSDK.EDS_ERR_INVALID_ID, "Invalid ID" },
        { EDSDK.EDS_ERR_WAIT_TIMEOUT_ERROR, "Timeout" },
        { EDSDK.EDS_ERR_LAST_GENERIC_ERROR_PLUS_ONE, "Not used." },

        // PTP errors
        { EDSDK.EDS_ERR_SESSION_NOT_OPEN, "Session open error" },
        { EDSDK.EDS_ERR_INVALID_TRANSACTIONID, "Invalid transaction ID" },
        { EDSDK.EDS_ERR_INCOMPLETE_TRANSFER, "Transfer problem" },
        { EDSDK.EDS_ERR_INVALID_STRAGEID, "Storage error" },
        { EDSDK.EDS_ERR_DEVICEPROP_NOT_SUPPORTED, "Unsupported device property" },
        { EDSDK.EDS_ERR_INVALID_OBJECTFORMATCODE, "Invalid object format code" },
        { EDSDK.EDS_ERR_SELF_TEST_FAILED, "Failed self-diagnosis" },
        { EDSDK.EDS_ERR_PARTIAL_DELETION, "Failed in partial deletion" },
        { EDSDK.EDS_ERR_SPECIFICATION_BY_FORMAT_UNSUPPORTED, "Unsupported format specification" },
        { EDSDK.EDS_ERR_NO_VALID_OBJECTINFO, "Invalid object information" },
        { EDSDK.EDS_ERR_INVALID_CODE_FORMAT, "Invalid code format" },
        { EDSDK.EDS_ERR_UNKNOWN_VENDER_CODE, "Unknown vendor code" }, // not defined in headers
        { EDSDK.EDS_ERR_CAPTURE_ALREADY_TERMINATED, "Capture already terminated" },
        { EDSDK.EDS_ERR_INVALID_PARENTOBJECT, "Invalid parent object" },
        { EDSDK.EDS_ERR_INVALID_DEVICEPROP_FORMAT, "Invalid property format" },
        { EDSDK.EDS_ERR_INVALID_DEVICEPROP_VALUE, "Invalid property value" },
        { EDSDK.EDS_ERR_SESSION_ALREADY_OPEN, "Session already open" },
        { EDSDK.EDS_ERR_TRANSACTION_CANCELLED, "Transaction canceled" },
        { EDSDK.EDS_ERR_SPECIFICATION_OF_DESTINATION_UNSUPPORTED, "Unsupported destination specification" },
        { EDSDK.EDS_ERR_UNKNOWN_COMMAND, "Unknown command" },
        { EDSDK.EDS_ERR_OPERATION_REFUSED, "Operation refused" },
        { EDSDK.EDS_ERR_LENS_COVER_CLOSE, "Lens cover closed" },
        { EDSDK.EDS_ERR_LOW_BATTERY, "Low battery" },
        { EDSDK.EDS_ERR_OBJECT_NOTREADY, "Image data set not ready for live view" },

        // Take picture errors
        { EDSDK.EDS_ERR_TAKE_PICTURE_AF_NG, "Focus failed" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_RESERVED, "Reserved" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_MIRROR_UP_NG, "Currently configuring mirror up" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_SENSOR_CLEANING_NG, "Currently cleaning sensor" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_SILENCE_NG, "Currently performing silent operations" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_NO_CARD_NG, "Card not installed" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_CARD_NG, "Error writing to card" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_CARD_PROTECT_NG, "Card is write protected" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_MOVIE_CROP_NG, "Failed in processing with movie crop" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_STROBO_CHARGE_NG, "Flash is charging" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_NO_LENS_NG, "Lens is not attached" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_SPECIAL_MOVIE_MODE_NG, "Movie camera exceeds the limit" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_LV_REL_PROHIBIT_MODE_NG, "Live view is not ready to take a picture in this shooting mode" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_MOVIE_MODE_NG, "Cannot take a still image while the camera is in movie mode" },
        { EDSDK.EDS_ERR_TAKE_PICTURE_RETRUCTED_LENS_NG, "Lens is retracted" },

        // Errors added in recent EDSDK versions
        { EDSDK.EDS_ERR_PTP_DEVICE_BUSY, "Device is busy" },
        { EDSDK.EDS_ERR_NOT_CAMERA_SUPPORT_SDK_VERSION, "Camera is not supported by this EDSDK version" },
        { EDSDK.EDS_ERR_CANNOT_MAKE_OBJECT, "Cannot make object" },
        { EDSDK.EDS_ERR_MEMORYSTATUS_NOTREADY, "Memory status not ready" }
    };

    public static string GetErrorMessage(uint errorCode) =>
        ErrorMessages.TryGetValue(errorCode, out var message) ? message : $"EDSDK error 0x{errorCode:X8}";

    /// <summary>
    /// "OK", the error message followed by its code, or the code alone when unknown.
    /// </summary>
    public static string DescribeResult(uint errorCode) =>
        errorCode == EDSDK.EDS_ERR_OK ? "OK"
        : ErrorMessages.TryGetValue(errorCode, out var message) ? $"{message} ({FormatRawValue(errorCode)})"
        : FormatRawValue(errorCode);

    // Property, object and state event constants of EDSDK, by value ("*_All" are subscription masks, not events).
    private static readonly Dictionary<uint, string> EventNames = typeof(EDSDK)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(uint) && !field.Name.EndsWith("_All")
            && (field.Name.StartsWith("PropertyEvent_") || field.Name.StartsWith("ObjectEvent_") || field.Name.StartsWith("StateEvent_")))
        .GroupBy(field => (uint)field.GetRawConstantValue()!)
        .ToDictionary(group => group.Key, group => group.First().Name);

    /// <summary>
    /// Name of an EDSDK event (e.g. "StateEvent_Shutdown"), or its code when unknown.
    /// </summary>
    public static string DescribeEvent(uint eventId) =>
        EventNames.TryGetValue(eventId, out var name) ? name : FormatRawValue(eventId);

    /// <summary>
    /// Values of kEdsPropID_Av (EDSDK API reference 5.2.25).
    /// "(1/3)" labels are the values used when the exposure step is set to 1/3 in the custom functions.
    /// </summary>
    private static readonly Dictionary<uint, string> AvValues = new()
    {
        { 0x08, "1" },
        { 0x0B, "1.1" },
        { 0x0C, "1.2" },
        { 0x0D, "1.2 (1/3)" },
        { 0x10, "1.4" },
        { 0x13, "1.6" },
        { 0x14, "1.8" },
        { 0x15, "1.8 (1/3)" },
        { 0x18, "2" },
        { 0x1B, "2.2" },
        { 0x1C, "2.5" },
        { 0x1D, "2.5 (1/3)" },
        { 0x20, "2.8" },
        { 0x23, "3.2" },
        { 0x85, "3.4" },
        { 0x24, "3.5" },
        { 0x25, "3.5 (1/3)" },
        { 0x28, "4" },
        { 0x2B, "4.5 (1/3)" },
        { 0x2C, "4.5" },
        { 0x2D, "5.0" },
        { 0x30, "5.6" },
        { 0x33, "6.3" },
        { 0x34, "6.7" },
        { 0x35, "7.1" },
        { 0x38, "8" },
        { 0x3B, "9" },
        { 0x3C, "9.5" },
        { 0x3D, "10" },
        { 0x40, "11" },
        { 0x43, "13 (1/3)" },
        { 0x44, "13" },
        { 0x45, "14" },
        { 0x48, "16" },
        { 0x4B, "18" },
        { 0x4C, "19" },
        { 0x4D, "20" },
        { 0x50, "22" },
        { 0x53, "25" },
        { 0x54, "27" },
        { 0x55, "29" },
        { 0x58, "32" },
        { 0x5B, "36" },
        { 0x5C, "38" },
        { 0x5D, "40" },
        { 0x60, "45" },
        { 0x63, "51" },
        { 0x64, "54" },
        { 0x65, "57" },
        { 0x68, "64" },
        { 0x6B, "72" },
        { 0x6C, "76" },
        { 0x6D, "80" },
        { 0x70, "91" }
    };

    /// <summary>
    /// Values of kEdsPropID_Tv (EDSDK API reference 5.2.26).
    /// Bulb cannot be set from a computer; it is only listed so the current value can be displayed.
    /// </summary>
    private static readonly Dictionary<uint, string> TvValues = new()
    {
        { 0x0C, "BULB" },
        { 0x10, "30\"" },
        { 0x13, "25\"" },
        { 0x14, "20\"" },
        { 0x15, "20\" (1/3)" },
        { 0x18, "15\"" },
        { 0x1B, "13\"" },
        { 0x1C, "10\"" },
        { 0x1D, "10\" (1/3)" },
        { 0x20, "8\"" },
        { 0x23, "6\" (1/3)" },
        { 0x24, "6\"" },
        { 0x25, "5\"" },
        { 0x28, "4\"" },
        { 0x2B, "3.2\"" },
        { 0x2C, "3\"" },
        { 0x2D, "2.5\"" },
        { 0x30, "2\"" },
        { 0x33, "1.6\"" },
        { 0x34, "1.5\"" },
        { 0x35, "1.3\"" },
        { 0x38, "1\"" },
        { 0x3B, "0.8\"" },
        { 0x3C, "0.7\"" },
        { 0x3D, "0.6\"" },
        { 0x40, "0.5\"" },
        { 0x43, "0.4\"" },
        { 0x44, "0.3\"" },
        { 0x45, "0.3\" (1/3)" },
        { 0x48, "1/4" },
        { 0x4B, "1/5" },
        { 0x4C, "1/6" },
        { 0x4D, "1/6 (1/3)" },
        { 0x50, "1/8" },
        { 0x53, "1/10 (1/3)" },
        { 0x54, "1/10" },
        { 0x55, "1/13" },
        { 0x58, "1/15" },
        { 0x5B, "1/20 (1/3)" },
        { 0x5C, "1/20" },
        { 0x5D, "1/25" },
        { 0x60, "1/30" },
        { 0x63, "1/40" },
        { 0x64, "1/45" },
        { 0x65, "1/50" },
        { 0x68, "1/60" },
        { 0x6B, "1/80" },
        { 0x6C, "1/90" },
        { 0x6D, "1/100" },
        { 0x70, "1/125" },
        { 0x73, "1/160" },
        { 0x74, "1/180" },
        { 0x75, "1/200" },
        { 0x78, "1/250" },
        { 0x7B, "1/320" },
        { 0x7C, "1/350" },
        { 0x7D, "1/400" },
        { 0x80, "1/500" },
        { 0x83, "1/640" },
        { 0x84, "1/750" },
        { 0x85, "1/800" },
        { 0x88, "1/1000" },
        { 0x8B, "1/1250" },
        { 0x8C, "1/1500" },
        { 0x8D, "1/1600" },
        { 0x90, "1/2000" },
        { 0x93, "1/2500" },
        { 0x94, "1/3000" },
        { 0x95, "1/3200" },
        { 0x98, "1/4000" },
        { 0x9B, "1/5000" },
        { 0x9C, "1/6000" },
        { 0x9D, "1/6400" },
        { 0xA0, "1/8000" },
        { 0xA3, "1/10000" },
        { 0xA5, "1/12800" },
        { 0xA8, "1/16000" },
        { 0xAB, "1/20000" },
        { 0xAD, "1/25600" },
        { 0xB0, "1/32000" }
    };

    /// <summary>
    /// Values of kEdsPropID_ISOSpeed (EDSDK API reference 5.2.22).
    /// </summary>
    private static readonly Dictionary<uint, string> ISOValues = new()
    {
        { 0x00, "Auto" },
        { 0x28, "6" },
        { 0x30, "12" },
        { 0x38, "25" },
        { 0x40, "50" },
        { 0x48, "100" },
        { 0x4B, "125" },
        { 0x4D, "160" },
        { 0x50, "200" },
        { 0x53, "250" },
        { 0x55, "320" },
        { 0x58, "400" },
        { 0x5B, "500" },
        { 0x5D, "640" },
        { 0x60, "800" },
        { 0x63, "1000" },
        { 0x65, "1250" },
        { 0x68, "1600" },
        { 0x6B, "2000" },
        { 0x6D, "2500" },
        { 0x70, "3200" },
        { 0x73, "4000" },
        { 0x75, "5000" },
        { 0x78, "6400" },
        { 0x7B, "8000" },
        { 0x7D, "10000" },
        { 0x80, "12800" },
        { 0x83, "16000" },
        { 0x85, "20000" },
        { 0x88, "25600" },
        { 0x8B, "32000" },
        { 0x8D, "40000" },
        { 0x90, "51200" },
        { 0x93, "64000" },
        { 0x95, "80000" },
        { 0x98, "102400" },
        { 0xA0, "204800" },
        { 0xA8, "409600" },
        { 0xB0, "819200" }
    };

    /// <summary>
    /// Values of kEdsPropID_WhiteBalance (EDSDK API reference 5.2.35).
    /// </summary>
    private static readonly Dictionary<uint, string> WhiteBalanceValues = new()
    {
        { 0, "Auto" },
        { 1, "Daylight" },
        { 2, "Cloudy" },
        { 3, "Tungsten" },
        { 4, "Fluorescent" },
        { 5, "Flash" },
        { 6, "Manual" },
        { 8, "Shade" },
        { 9, "Color Temp" },
        { 10, "Custom" },
        { 11, "Custom 2" },
        { 12, "Custom 3" },
        { 15, "Manual 2" },
        { 16, "Manual 3" },
        { 18, "Manual 4" },
        { 19, "Manual 5" },
        { 20, "Custom 4" },
        { 21, "Custom 5" },
        { 23, "Auto (White priority)" },
        { 24, "Color Temp 2" },
        { 25, "Color Temp 3" },
        { 26, "Color Temp 4" }
    };

    /// <summary>
    /// Values of kEdsPropID_ExposureCompensation (EDSDK API reference 5.2.28).
    /// Not available in manual exposure mode.
    /// </summary>
    private static readonly Dictionary<uint, string> ExposureCompensationValues = new()
    {
        { 0x28, "+5" },
        { 0x25, "+4 2/3" },
        { 0x24, "+4 1/2" },
        { 0x23, "+4 1/3" },
        { 0x20, "+4" },
        { 0x1D, "+3 2/3" },
        { 0x1C, "+3 1/2" },
        { 0x1B, "+3 1/3" },
        { 0x18, "+3" },
        { 0x15, "+2 2/3" },
        { 0x14, "+2 1/2" },
        { 0x13, "+2 1/3" },
        { 0x10, "+2" },
        { 0x0D, "+1 2/3" },
        { 0x0C, "+1 1/2" },
        { 0x0B, "+1 1/3" },
        { 0x08, "+1" },
        { 0x05, "+2/3" },
        { 0x04, "+1/2" },
        { 0x03, "+1/3" },
        { 0x00, "0" },
        { 0xFD, "-1/3" },
        { 0xFC, "-1/2" },
        { 0xFB, "-2/3" },
        { 0xF8, "-1" },
        { 0xF5, "-1 1/3" },
        { 0xF4, "-1 1/2" },
        { 0xF3, "-1 2/3" },
        { 0xF0, "-2" },
        { 0xED, "-2 1/3" },
        { 0xEC, "-2 1/2" },
        { 0xEB, "-2 2/3" },
        { 0xE8, "-3" },
        { 0xE5, "-3 1/3" },
        { 0xE4, "-3 1/2" },
        { 0xE3, "-3 2/3" },
        { 0xE0, "-4" },
        { 0xDD, "-4 1/3" },
        { 0xDC, "-4 1/2" },
        { 0xDB, "-4 2/3" },
        { 0xD8, "-5" }
    };

    /// <summary>
    /// Values of kEdsPropID_AEMode (EDSDK API reference 5.2.19).
    /// </summary>
    internal static readonly Dictionary<uint, string> AEModeValues = new()
    {
        { 0x00, "Program AE" },
        { 0x01, "Shutter-Speed Priority AE" },
        { 0x02, "Aperture Priority AE" },
        { 0x03, "Manual Exposure" },
        { 0x04, "Bulb" },
        { 0x05, "Auto Depth-of-Field AE" },
        { 0x06, "Depth-of-Field AE" },
        { 0x07, "Camera settings registered" },
        { 0x08, "Lock" },
        { 0x09, "Auto" },
        { 0x0A, "Night Scene Portrait" },
        { 0x0B, "Sports" },
        { 0x0C, "Portrait" },
        { 0x0D, "Landscape" },
        { 0x0E, "Close-Up" },
        { 0x0F, "Flash Off" },
        { 0x13, "Creative Auto" },
        { 0x14, "Movies" },
        { 0x15, "Photo In Movie" },
        { 0x16, "Scene Intelligent Auto" },
        { 0x17, "Night Scenes" },
        { 0x18, "Backlit Scenes" },
        { 0x1A, "Kids" },
        { 0x1B, "Food" },
        { 0x1C, "Candlelight" },
        { 0x1E, "Grainy B/W" },
        { 0x1F, "Soft focus" },
        { 0x20, "Toy camera effect" },
        { 0x21, "Fish-eye effect" },
        { 0x22, "Water painting effect" },
        { 0x23, "Miniature effect" },
        { 0x24, "HDR art standard" },
        { 0x25, "HDR art vivid" },
        { 0x26, "HDR art bold" },
        { 0x27, "HDR art embossed" },
        { 0x28, "Dream" },
        { 0x29, "Old Movies" },
        { 0x2A, "Memory" },
        { 0x2B, "Dramatic B&W" },
        { 0x2C, "Miniature effect movie" },
        { 0x2D, "Panning" },
        { 0x2E, "Group Photo" },
        { 0x32, "Myself (Self Portrait)" },
        { 0x33, "Plus Movie Auto" },
        { 0x34, "SmoothSkin" },
        { 0x36, "Silent Mode" },
        { 0x37, "Flexible-priority AE" },
        { 0x38, "Oil painting (Art bold effect)" },
        { 0x39, "Fireworks" },
        { 0x3A, "Star portrait" },
        { 0x3B, "Star nightscape" },
        { 0x3C, "Star trails" },
        { 0x3D, "Star time-lapse movie" },
        { 0x3E, "Background blur" },
        { 0x3F, "VideoBlog" },
        { 0x41, "Movie IS mode" },
        { 0x43, "Smooth skin movie" }
    };

    /// <summary>
    /// AE modes of the "creative zone", in which exposure settings (ISO, Av, Tv, ...) can be changed remotely.
    /// In the other ("basic zone") modes the camera sets capture-related properties automatically.
    /// </summary>
    internal static readonly HashSet<uint> CreativeZoneAEModes = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x37];

    private static readonly Dictionary<uint, Dictionary<string, uint>> DescriptionsByProperty = new()
    {
        { EDSDK.PropID_Av, Reverse(AvValues) },
        { EDSDK.PropID_Tv, Reverse(TvValues) },
        { EDSDK.PropID_ISOSpeed, Reverse(ISOValues) },
        { EDSDK.PropID_WhiteBalance, Reverse(WhiteBalanceValues) },
        { EDSDK.PropID_ExposureCompensation, Reverse(ExposureCompensationValues) },
    };

    private static Dictionary<string, uint> Reverse(Dictionary<uint, string> values) =>
        values.ToDictionary(v => v.Value, v => v.Key, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<uint, string> GetPropertyValues(this uint propId) => propId switch
    {
        EDSDK.PropID_Av => AvValues,
        EDSDK.PropID_Tv => TvValues,
        EDSDK.PropID_ISOSpeed => ISOValues,
        EDSDK.PropID_WhiteBalance => WhiteBalanceValues,
        EDSDK.PropID_ExposureCompensation => ExposureCompensationValues,
        _ => throw new ArgumentOutOfRangeException(nameof(propId), $"Unsupported property 0x{propId:X}")
    };

    public static Dictionary<string, uint> GetPropertyDescriptions(this uint propId) =>
        DescriptionsByProperty.TryGetValue(propId, out var descriptions)
            ? descriptions
            : throw new ArgumentOutOfRangeException(nameof(propId), $"Unsupported property 0x{propId:X}");

    /// <summary>
    /// Value returned for ISO, Av, Tv and exposure compensation when the setting is not valid in the current state
    /// (e.g. exposure compensation in manual exposure mode): "Not valid/no settings changes" (EDSDK API reference 5.2.22 to 5.2.28).
    /// It is only a label for reading: it cannot be sent back.
    /// </summary>
    public const uint NotValidValue = 0xFFFFFFFF;

    public const string NotValidLabel = "Not valid";

    /// <summary>
    /// Returns the human readable label of a property value, or the raw value (e.g. "0x93") when the value is unknown.
    /// </summary>
    public static string DescribeValue(this uint propId, uint value)
    {
        if (propId.GetPropertyValues().TryGetValue(value, out var description))
            return description;

        return value == NotValidValue && propId is EDSDK.PropID_ISOSpeed or EDSDK.PropID_Av or EDSDK.PropID_Tv or EDSDK.PropID_ExposureCompensation
            ? NotValidLabel
            : FormatRawValue(value);
    }

    public static string FormatRawValue(uint value) => $"0x{value:X}";

    /// <summary>
    /// Parses a property value from its label (case-insensitive) or from a raw value ("147", "0x93").
    /// </summary>
    public static bool TryParseValue(this uint propId, string text, out uint value)
    {
        text = text.Trim();

        if (propId.GetPropertyDescriptions().TryGetValue(text, out value))
            return true;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value);

        // Plain integers are only accepted for properties whose labels are not numbers themselves (ISO "100", Av "8"...)
        // to avoid ambiguities between a label and a raw value.
        if (propId is EDSDK.PropID_WhiteBalance)
            return uint.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);

        return false;
    }

    private const string OneThirdStopSuffix = " (1/3)";

    /// <summary>
    /// Values of the other exposure step whose label reads the same as <paramref name="label"/>, e.g. aperture "2.5"
    /// gives 0x1D ("2.5 (1/3)") and "2.5 (1/3)" gives 0x1C ("2.5"). Empty for raw values and labels without a pair.
    /// </summary>
    public static IEnumerable<uint> GetSameLabelValues(this uint propId, string label)
    {
        label = label.Trim();
        var pair = label.EndsWith(OneThirdStopSuffix, StringComparison.OrdinalIgnoreCase)
            ? label[..^OneThirdStopSuffix.Length]
            : label + OneThirdStopSuffix;

        return DescriptionsByProperty.TryGetValue(propId, out var descriptions) && descriptions.TryGetValue(pair, out var value)
            ? [value]
            : [];
    }

    /// <summary>
    /// Gets the exposure duration in seconds of a kEdsPropID_Tv value. Returns false for Bulb and unknown values.
    /// </summary>
    public static bool TryGetExposureSeconds(uint tvValue, out double seconds)
    {
        seconds = 0;

        if (!TvValues.TryGetValue(tvValue, out var label) || tvValue == 0x0C)
            return false;

        label = label.Replace(OneThirdStopSuffix, string.Empty);
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        if (label.EndsWith('"'))
            return double.TryParse(label.TrimEnd('"'), System.Globalization.NumberStyles.Float, culture, out seconds);

        var parts = label.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, culture, out var numerator)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, culture, out var denominator)
            && denominator > 0)
        {
            seconds = numerator / denominator;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Translates the parameter of kEdsStateEvent_CaptureError (EDSDK API reference 4.2.17).
    /// </summary>
    public static string GetCaptureErrorMessage(uint errorCode) => errorCode switch
    {
        0x00000001 => "Shooting failure (e.g. focus failure)",
        0x00000002 => "Lens cover was closed",
        0x00000003 => "General shooting error (Bulb or mirror-up)",
        0x00000004 => "Camera is busy cleaning the sensor",
        0x00000005 => "Camera is set to silent operation",
        0x00000006 => "No card inserted",
        0x00000007 => "Card error (full or other)",
        0x00000008 => "Card write-protected",
        _ => $"Unknown capture error: 0x{errorCode:X}"
    };
}
