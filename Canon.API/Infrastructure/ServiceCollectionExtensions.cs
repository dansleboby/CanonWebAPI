using Canon.Core;
using Microsoft.Extensions.Options;

namespace Canon.API.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the camera, the live view broadcaster, the error handling and the OpenAPI document.
    /// </summary>
    public static IServiceCollection AddCanonApi(this IServiceCollection services, IConfiguration configuration, Version? version)
    {
        services.AddControllers();
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Canon Web API";
                document.Info.Version = version?.ToString(3) ?? "1.0.0";
                document.Info.Description = "Remote control of a Canon camera through the Canon EDSDK: settings, capture and MJPEG live view. " +
                                            "Errors are returned as problem details: 400 invalid value, 409 capture refused by the camera or setting not changeable in the current shooting mode, " +
                                            "503 camera not connected or busy, 504 capture timeout.";
                return Task.CompletedTask;
            });
        });
        services.AddProblemDetails();
        services.AddExceptionHandler<CameraExceptionHandler>();

        services.Configure<CanonCameraOptions>(configuration.GetSection(CanonCameraOptions.SectionName));
        services.Configure<LiveViewBroadcasterOptions>(configuration.GetSection(LiveViewBroadcasterOptions.SectionName));

        services.AddSingleton(sp => new CanonCamera(
            sp.GetRequiredService<ILogger<CanonCamera>>(),
            sp.GetRequiredService<IOptions<CanonCameraOptions>>().Value));

        services.AddSingleton(sp => new LiveViewBroadcaster(
            sp.GetRequiredService<CanonCamera>(),
            sp.GetRequiredService<ILogger<LiveViewBroadcaster>>(),
            sp.GetRequiredService<IOptions<LiveViewBroadcasterOptions>>().Value));

        services.AddHostedService<CameraStartupService>();

        return services;
    }
}
