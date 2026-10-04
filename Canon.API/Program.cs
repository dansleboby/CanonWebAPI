using Canon.API.Infrastructure;
using Serilog;
#if WINDOWS
using AutoUpdaterDotNET;
#endif

// Next to the executable, whatever the working directory (shortcut, scheduled task...).
var logFile = Path.Combine(AppContext.BaseDirectory, "logs", "canon-api.log");

// Startup logger, replaced by the configured one once the host is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

// Display application version
var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
Log.Information("CanonWebAPI v{Version} starting...", version);

try
{
    // Settings files are read next to the executable, whatever the working directory.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

    // Operator settings. The release package ships appsettings.json, which every automatic update overwrites;
    // this file is never shipped. Environment variables and the command line keep precedence over it.
    builder.Configuration
        .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .AddCommandLine(args);

    // This overload reloads the startup logger, which closes its log file before the configured logger opens it.
    builder.Services.AddSerilog((_, logger) => logger
        .ApplyLogLevels(builder.Configuration)
        .WriteTo.Console()
        .WriteTo.File(logFile, rollingInterval: RollingInterval.Day));
    builder.Services.AddCanonApi(builder.Configuration, version);

    // The API only listens on the local machine, so any origin is allowed.
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders("X-File-Name", "Retry-After");
        });
    });

    var app = builder.Build();

#if WINDOWS
    if (app.Configuration.GetValue("AutoUpdate:Enabled", true))
        ConfigureAutoUpdater(app);
    else
        Log.Information("Automatic updates are disabled");
#else
    Log.Information("Automatic updates are not available on this platform");
#endif

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

#if WINDOWS
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

    // The check is synchronous and runs before app.Run(): the web server is not started and the camera not opened yet,
    // so there is nothing to stop before the updater replaces the files.
    AutoUpdater.ApplicationExitEvent += () =>
    {
        Log.Information("AutoUpdater requesting application exit for update...");
        Log.CloseAndFlush();
        Environment.Exit(0);
    };

    AutoUpdater.Start(app.Configuration["AutoUpdate:Url"] ?? "https://dansleboby.github.io/CanonWebAPI/autoupdate.xml");
}
#endif
