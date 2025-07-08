namespace Canon.Core;

public class EdsException(uint errorCode, string message) : Exception($"{message}: {(EdsdkHelper.ErrorMessages.TryGetValue(errorCode, out var m) ? m : errorCode.ToString())}")
{
    public uint ErrorCode { get; } = errorCode;
}