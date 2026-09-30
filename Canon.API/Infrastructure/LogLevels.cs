using Serilog;
using Serilog.Events;

namespace Canon.API.Infrastructure;

public static class LogLevels
{
    /// <summary>
    /// Applies the "Logging:LogLevel" section (Microsoft format: "Default" and category prefixes) to Serilog, which
    /// replaces the Microsoft logging pipeline and ignores that section otherwise.
    /// </summary>
    public static LoggerConfiguration ApplyLogLevels(this LoggerConfiguration logger, IConfiguration configuration)
    {
        foreach (var entry in configuration.GetSection("Logging:LogLevel").GetChildren())
        {
            if (!Enum.TryParse<LogLevel>(entry.Value, ignoreCase: true, out var level))
                continue;

            var serilogLevel = ToSerilog(level);

            if (string.Equals(entry.Key, "Default", StringComparison.OrdinalIgnoreCase))
                logger.MinimumLevel.Is(serilogLevel);
            else
                logger.MinimumLevel.Override(entry.Key, serilogLevel);
        }

        return logger;
    }

    // Serilog has no level above Fatal: "None" keeps only fatal events.
    private static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Fatal
    };
}
