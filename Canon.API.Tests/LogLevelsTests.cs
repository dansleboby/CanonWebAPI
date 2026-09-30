using Canon.API.Infrastructure;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Canon.API.Tests;

public class LogLevelsTests
{
    private static Logger Create(Dictionary<string, string?> levels) =>
        new LoggerConfiguration()
            .ApplyLogLevels(new ConfigurationBuilder()
                .AddInMemoryCollection(levels.ToDictionary(l => $"Logging:LogLevel:{l.Key}", l => l.Value))
                .Build())
            .CreateLogger();

    private static bool IsEnabled(Logger logger, string category, LogEventLevel level) =>
        logger.ForContext(Constants.SourceContextPropertyName, category).IsEnabled(level);

    [Fact]
    public void Category_levels_of_appsettings_apply()
    {
        using var logger = Create(new() { ["Default"] = "Information", ["Microsoft.AspNetCore"] = "Warning" });

        Assert.True(IsEnabled(logger, "Canon.Core.CanonCamera", LogEventLevel.Information));
        Assert.False(IsEnabled(logger, "Canon.Core.CanonCamera", LogEventLevel.Debug));
        Assert.False(IsEnabled(logger, "Microsoft.AspNetCore.Routing.EndpointMiddleware", LogEventLevel.Information));
        Assert.True(IsEnabled(logger, "Microsoft.AspNetCore.Routing.EndpointMiddleware", LogEventLevel.Warning));
    }

    [Fact]
    public void Default_level_can_be_lowered_and_invalid_levels_are_ignored()
    {
        using var logger = Create(new() { ["Default"] = "Debug", ["Canon"] = "loud" });

        Assert.True(IsEnabled(logger, "Canon.Core.CanonCamera", LogEventLevel.Debug));
    }

    [Fact]
    public void None_keeps_only_fatal_events()
    {
        using var logger = Create(new() { ["Default"] = "None" });

        Assert.False(logger.IsEnabled(LogEventLevel.Error));
        Assert.True(logger.IsEnabled(LogEventLevel.Fatal));
    }
}
