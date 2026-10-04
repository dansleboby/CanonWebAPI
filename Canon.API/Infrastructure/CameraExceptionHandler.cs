using Canon.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Canon.API.Infrastructure;

/// <summary>
/// Converts camera errors into RFC 7807 problem details with a meaningful HTTP status code.
/// </summary>
public sealed class CameraExceptionHandler(ILogger<CameraExceptionHandler> logger, IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
            return false;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away: nothing to answer.
            return true;
        }

        var (status, title) = Map(exception);

        if (status >= 500 && status != StatusCodes.Status503ServiceUnavailable && status != StatusCodes.Status504GatewayTimeout)
            logger.LogError(exception, "Unhandled error on {Path}", httpContext.Request.Path);
        else
            logger.LogWarning("{Path}: {Title} - {Message}", httpContext.Request.Path, title, exception.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path
        };

        switch (exception)
        {
            case EdsException eds:
                problem.Extensions["errorCode"] = $"0x{eds.ErrorCode:X8}";
                break;
            case CaptureFailedException capture:
                problem.Extensions["captureErrorCode"] = capture.CaptureErrorCode;
                break;
            case CameraSettingsException settings:
                AddSettingsExtensions(problem, settings);
                break;
            case CameraSettingsApplyException apply:
                problem.Extensions["applied"] = apply.Applied.Select(p => p.ToSettingName()).ToArray();
                problem.Extensions["failed"] = new
                {
                    property = apply.Failed.ToSettingName(),
                    value = apply.FailedValue,
                    detail = apply.InnerException?.Message,
                    errorCode = apply.InnerException is EdsException inner ? $"0x{inner.ErrorCode:X8}" : null
                };
                if (apply.InnerException is EdsException innerEds)
                    problem.Extensions["errorCode"] = $"0x{innerEds.ErrorCode:X8}";
                break;
        }

        if (exception is EdsException { IsBusy: true } or CameraSettingsApplyException { InnerException: EdsException { IsBusy: true } })
            httpContext.Response.Headers.RetryAfter = "1";

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    /// <summary>
    /// Machine readable description of refused settings: the decisive error at the top level (reason, property,
    /// acceptedValues), the shooting mode, and every refused setting in "errors".
    /// </summary>
    private static void AddSettingsExtensions(ProblemDetails problem, CameraSettingsException exception)
    {
        var primary = exception.PrimaryError;

        problem.Extensions["reason"] = primary.Reason.ToCode();
        problem.Extensions["property"] = primary.Property.ToSettingName();
        problem.Extensions["acceptedValues"] = primary.AcceptedValues;
        problem.Extensions["aeMode"] = exception.Mode.AEMode;
        problem.Extensions["aeModeCode"] = exception.Mode.AEModeCode;
        problem.Extensions["errors"] = exception.Errors.Select(e => new
        {
            property = e.Property.ToSettingName(),
            value = e.RequestedValue,
            reason = e.Reason.ToCode(),
            acceptedValues = e.AcceptedValues,
            detail = e.Message
        }).ToArray();
    }

    /// <summary>
    /// HTTP status code and title for an exception.
    /// </summary>
    public static (int Status, string Title) Map(Exception exception) => exception switch
    {
        CameraSettingsException { PrimaryError.Reason: SettingErrorReason.NotSettableInMode } => (StatusCodes.Status409Conflict, "Setting cannot be changed in the current shooting mode"),
        CameraSettingsException { PrimaryError.Reason: SettingErrorReason.ValueNotAccepted } => (StatusCodes.Status400BadRequest, "Value not accepted by the camera"),
        CameraSettingsException => (StatusCodes.Status400BadRequest, "Invalid value"),
        CameraSettingsApplyException { InnerException: { } inner } => (Map(inner).Status, "Settings not fully applied"),
        ArgumentException => (StatusCodes.Status400BadRequest, "Invalid value"),
        CaptureFailedException => (StatusCodes.Status409Conflict, "Capture failed"),
        TimeoutException => (StatusCodes.Status504GatewayTimeout, "Camera did not respond in time"),
        CameraNotConnectedException => (StatusCodes.Status503ServiceUnavailable, "Camera not connected"),
        EdsException { IsBusy: true } => (StatusCodes.Status503ServiceUnavailable, "Camera busy"),
        EdsException { IsDisconnected: true } => (StatusCodes.Status503ServiceUnavailable, "Camera not connected"),
        EdsException { IsTakePictureError: true } => (StatusCodes.Status409Conflict, "Capture failed"),
        EdsException { IsRefused: true } => (StatusCodes.Status409Conflict, "Operation refused by the camera"),
        EdsException { IsInvalidValue: true } => (StatusCodes.Status400BadRequest, "Value not accepted by the camera"),
        EdsException => (StatusCodes.Status500InternalServerError, "Camera error"),
        DllNotFoundException or BadImageFormatException => (StatusCodes.Status500InternalServerError, "Canon EDSDK could not be loaded (EDSDK.dll or libEDSDK.so missing, wrong architecture, or libusb-1.0 missing on Linux)"),
        _ => (StatusCodes.Status500InternalServerError, "Internal server error")
    };
}
