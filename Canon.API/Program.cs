using Canon.Core;
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
    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddSwaggerGen();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    builder.Services.AddSingleton<CanonCamera>();

    var app = builder.Build();

    // Configure AutoUpdater.NET for automatic updates
    AutoUpdater.Mandatory = true;
    AutoUpdater.UpdateMode = Mode.ForcedDownload;
    AutoUpdater.Synchronous = true;
    AutoUpdater.RunUpdateAsAdmin = false; // Disable admin requirement
    
    // Set download path to application directory to avoid temp folder permission issues
    var appDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
    AutoUpdater.DownloadPath = appDirectory;
    AutoUpdater.InstallationPath = appDirectory;
    
    // Handle application exit for updates - properly shutdown web server
    AutoUpdater.ApplicationExitEvent += () =>
    {
        Log.Information("AutoUpdater requesting application exit for update...");
        
        // Give the web server time to complete current requests
        var cancellationTokenSource = new CancellationTokenSource();
        var shutdownTask = Task.Run(async () =>
        {
            try
            {
                // Stop accepting new requests
                await app.StopAsync(cancellationTokenSource.Token);
                Log.Information("Web server stopped gracefully");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during graceful shutdown, forcing exit");
            }
            finally
            {
                Environment.Exit(0);
            }
        });
        
        // Wait maximum 10 seconds for graceful shutdown
        if (!shutdownTask.Wait(10000))
        {
            Log.Warning("Graceful shutdown timed out, forcing exit");
            cancellationTokenSource.Cancel();
            Environment.Exit(0);
        }
    };
    
    AutoUpdater.Start("https://dansleboby.github.io/CanonWebAPI/autoupdate.xml");

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseCors();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    Log.CloseAndFlush();
}