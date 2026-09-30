using Canon.API.Infrastructure;
using Serilog;
using AutoUpdaterDotNET;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/canon-api.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

// Display application version
var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
Log.Information("CanonWebAPI v{Version} starting...", version);

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog();
    builder.Services.AddCanonApi(builder.Configuration, version);

    // The API only listens on the local machine, so any origin is allowed.
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders("X-File-Name");
        });
    });

    var app = builder.Build();

    if (app.Configuration.GetValue("AutoUpdate:Enabled", true))
        ConfigureAutoUpdater(app);
    else
        Log.Information("Automatic updates are disabled");

    app.UseExceptionHandler();

    // OpenAPI document at /openapi/v1.json, Swagger UI at /swagger
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Canon Web API"));

    app.UseHttpsRedirection();
    app.UseCors();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    Log.CloseAndFlush();
}

static void ConfigureAutoUpdater(WebApplication app)
{
    // Configure AutoUpdater.NET for automatic updates
    AutoUpdater.Mandatory = true;
    AutoUpdater.UpdateMode = Mode.ForcedDownload;
    AutoUpdater.Synchronous = true;
    AutoUpdater.RunUpdateAsAdmin = false; // Disable admin requirement

    // Set download path to application directory to avoid temp folder permission issues
    var appDirectory = AppContext.BaseDirectory;
    AutoUpdater.DownloadPath = appDirectory;
    AutoUpdater.InstallationPath = appDirectory;

    // Handle application exit for updates - properly shutdown web server and release the camera
    AutoUpdater.ApplicationExitEvent += () =>
    {
        Log.Information("AutoUpdater requesting application exit for update...");

        var shutdownTask = Task.Run(async () =>
        {
            try
            {
                // Stop accepting new requests, then dispose services: stops the live view,
                // closes the camera session and terminates the SDK.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await app.StopAsync(timeout.Token);
                await app.DisposeAsync();
                Log.Information("Web server stopped gracefully");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during graceful shutdown, forcing exit");
            }
            finally
            {
                Log.CloseAndFlush();
                Environment.Exit(0);
            }
        });

        // Wait maximum 10 seconds for graceful shutdown
        if (!shutdownTask.Wait(10000))
        {
            Log.Warning("Graceful shutdown timed out, forcing exit");
            Environment.Exit(0);
        }
    };

    AutoUpdater.Start(app.Configuration["AutoUpdate:Url"] ?? "https://dansleboby.github.io/CanonWebAPI/autoupdate.xml");
}
