using Serilog;
using Serilog.Events;
using AGI_PDM.Configuration;

namespace AGI_PDM.Utils;

public static class Logger
{
    public static void InitializeLogger(LoggingSettings settings)
    {
        var logDirectory = settings.LogPath;
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var logLevel = Enum.TryParse<LogEventLevel>(settings.LogLevel, out var level) 
            ? level 
            : LogEventLevel.Information;

        var logPath = Path.Combine(logDirectory, $"agi-pdm-{DateTime.Now:yyyy-MM-dd}.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 100 * 1024 * 1024, // 100MB default
                retainedFileCountLimit: 30)
            .CreateLogger();

        Log.Information("Logger initialized. Log file: {LogPath}", logPath);
    }

    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
    }
}