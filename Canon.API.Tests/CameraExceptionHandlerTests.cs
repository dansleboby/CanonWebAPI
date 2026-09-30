using System.Text.Json;
using Canon.API.Infrastructure;
using Canon.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Canon.API.Tests;

public class CameraExceptionHandlerTests
{
    private static readonly CameraMode Av = new(0x02, "Aperture Priority AE", true, false);

    public static TheoryData<Exception, int> StatusCodesByException => new()
    {
        { new CameraNotConnectedException("x"), 503 },
        { new EdsException(EDSDK.EDS_ERR_DEVICE_BUSY, "x"), 503 },
        { new EdsException(EDSDK.EDS_ERR_COMM_DISCONNECTED, "x"), 503 },
        { new TimeoutException(), 504 },
        { new CaptureFailedException(1), 409 },
        { new EdsException(EDSDK.EDS_ERR_TAKE_PICTURE_AF_NG, "x"), 409 },
        { new EdsException(EDSDK.EDS_ERR_OPERATION_REFUSED, "x"), 409 },
        { new EdsException(EDSDK.EDS_ERR_INVALID_DEVICEPROP_VALUE, "x"), 400 },
        { new EdsException(EDSDK.EDS_ERR_INTERNAL_ERROR, "x"), 500 },
        { new ArgumentException("x"), 400 },
        { Refused(SettingErrorReason.NotSettableInMode), 409 },
        { Refused(SettingErrorReason.ValueNotAccepted), 400 },
        { Refused(SettingErrorReason.InvalidValue), 400 },
        { NotApplied(new EdsException(EDSDK.EDS_ERR_DEVICE_BUSY, "x")), 503 },
        { NotApplied(new EdsException(EDSDK.EDS_ERR_INVALID_DEVICEPROP_VALUE, "x")), 400 },
        { new InvalidOperationException(), 500 },
    };

    private static CameraSettingsException Refused(SettingErrorReason reason) =>
        new(Av, [new SettingError(CameraProperty.ShutterSpeed, "1/125", reason, ["1/60"], "refused")]);

    private static CameraSettingsApplyException NotApplied(Exception inner) =>
        new([CameraProperty.ISOSpeed], CameraProperty.Aperture, "5.6", inner);

    [Theory]
    [MemberData(nameof(StatusCodesByException))]
    public void Exceptions_map_to_the_documented_status(Exception exception, int status)
    {
        Assert.Equal(status, CameraExceptionHandler.Map(exception).Status);
    }

    [Fact]
    public async Task Busy_camera_answers_503_with_retry_after()
    {
        var (context, body) = await Handle(new EdsException(EDSDK.EDS_ERR_DEVICE_BUSY, "Could not set ISO"));

        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("0x00000081", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Busy_camera_while_applying_settings_answers_503_with_retry_after_and_the_applied_settings()
    {
        var (context, body) = await Handle(NotApplied(new EdsException(EDSDK.EDS_ERR_DEVICE_BUSY, "x")));

        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal(["iso"], body.GetProperty("applied").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("aperture", body.GetProperty("failed").GetProperty("property").GetString());
    }

    [Fact]
    public async Task Refused_settings_are_machine_readable()
    {
        var (context, body) = await Handle(Refused(SettingErrorReason.NotSettableInMode));

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("not-settable-in-mode", body.GetProperty("reason").GetString());
        Assert.Equal("shutterSpeed", body.GetProperty("property").GetString());
        Assert.Equal("Aperture Priority AE", body.GetProperty("aeMode").GetString());
        Assert.Equal(1, body.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task Nothing_is_written_when_the_client_went_away()
    {
        var context = new DefaultHttpContext { RequestAborted = new CancellationToken(true) };
        var handler = CreateHandler(out _);

        Assert.True(await handler.TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None));
        Assert.Equal(200, context.Response.StatusCode);
    }

    private static CameraExceptionHandler CreateHandler(out IServiceProvider services)
    {
        services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        return new CameraExceptionHandler(NullLogger<CameraExceptionHandler>.Instance, services.GetRequiredService<IProblemDetailsService>());
    }

    private static async Task<(HttpContext Context, JsonElement Body)> Handle(Exception exception)
    {
        var handler = CreateHandler(out var services);
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/test";
        context.Response.Body = new MemoryStream();

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        context.Response.Body.Position = 0;
        return (context, (await JsonDocument.ParseAsync(context.Response.Body)).RootElement);
    }
}
