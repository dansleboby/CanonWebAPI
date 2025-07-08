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
        { EDSDK.EDS_ERR_TAKE_PICTURE_CARD_PROTECT_NG, "Card is write protected" }
    };

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
        { 0x24, "3.5" },
        { 0x25, "3.5 (1/3)" },
        { 0x28, "4" },
        { 0x2B, "4.5" },
        { 0x2C, "4.5 (1/3)" },
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
        { 0xA0, "1/8000" }
    };

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
        { 0x98, "102400" }
    };

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
        { 16, "Manual 3" }
    };

    private static readonly Dictionary<uint, string> ExposureCompensationValues = new()
    {
        { 0x28, "+5 0xFD –1/3" },
        { 0x25, "+4 2/3 0xFC –1/2" },
        { 0x24, "+4 1/2 0xFB –2/3" },
        { 0x23, "+4 1/3 0xF8 –1" },
        { 0x20, "+4 0xF5 –1 1/3" },
        { 0x1D, "+3 2/3 0xF4 –1 1/2" },
        { 0x1C, "+3 1/2 0xF3 –1 2/3" },
        { 0x1B, "+3 1/3 0xF0 –2" },
        { 0x18, "+3 0xED –2 1/3" },
        { 0x15, "+2 2/3 0xEC –2 1/2" },
        { 0x14, "+2 1/2 0xEB –2 2/3 " }
    };
    
    private static Dictionary<string, uint> AvByName { get; } = AvValues.ToDictionary(v => v.Value, v => v.Key);

    private static Dictionary<string, uint> TvByName { get; } = TvValues.ToDictionary(v => v.Value, v => v.Key);

    private static Dictionary<string, uint> ISOByName { get; } = ISOValues.ToDictionary(v => v.Value, v => v.Key);

    private static Dictionary<string, uint> WhiteBalanceByName { get; } = WhiteBalanceValues.ToDictionary(v => v.Value, v => v.Key);
    
    private static Dictionary<string, uint> ExposureCompensationByName { get; } = ExposureCompensationValues.ToDictionary(v => v.Value, v => v.Key);
    
    public static Dictionary<uint, string> GetPropertyValues(this uint propId)
    {
        switch (propId)
        {
            case EDSDK.PropID_Av: return AvValues;
            case EDSDK.PropID_Tv: return TvValues;
            case EDSDK.PropID_ISOSpeed: return ISOValues;
            case EDSDK.PropID_WhiteBalance: return WhiteBalanceValues;
            case EDSDK.PropID_ExposureCompensation: return ExposureCompensationValues;

            default: throw new ArgumentOutOfRangeException();
        }
    }

    public static Dictionary<string, uint> GetPropertyDescriptions(this uint propId)
    {
        switch (propId)
        {
            case EDSDK.PropID_Av: return AvByName;
            case EDSDK.PropID_Tv: return TvByName;
            case EDSDK.PropID_ISOSpeed: return ISOByName;
            case EDSDK.PropID_WhiteBalance: return WhiteBalanceByName;
            case EDSDK.PropID_ExposureCompensation: return ExposureCompensationByName;

            default: throw new NotSupportedException();
        }
    }
}