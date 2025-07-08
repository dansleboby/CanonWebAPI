namespace Canon.Core;

public class EdsException(uint errorCode, string message, Exception? innerException) : Exception($"{message}: {(EdsdkHelper.ErrorMessages.TryGetValue(errorCode, out var m) ? m : errorCode.ToString())}", innerException)
{
    public uint ErrorCode { get; } = errorCode;
}