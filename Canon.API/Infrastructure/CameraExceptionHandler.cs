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
        }

        if (exception is EdsException { IsBusy: true })
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
    /// HTTP status code and title for an exception.
    /// </summary>
    public static (int Status, string Title) Map(Exception exception) => exception switch
    {
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
        DllNotFoundException or BadImageFormatException => (StatusCodes.Status500InternalServerError, "Canon EDSDK could not be loaded (EDSDK.dll missing or wrong architecture)"),
        _ => (StatusCodes.Status500InternalServerError, "Internal server error")
    };
}
