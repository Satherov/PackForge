using System;
using System.Collections.ObjectModel;
using System.IO;
using Serilog;
using Serilog.Events;

namespace PackForge.Logger;

public static class GlobalLog
{
    private static readonly string LogDataPath = Path.Combine(App.AppDataPath, "logs");

    public record LogEntry(DateTime Timestamp, LogEventLevel Level, string Message);

    public static ObservableCollection<LogEntry> LogEntries { get; } = [];

    public static void Initialize()
    {
        try
        {
            Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo
                .File(new LogLevelTextFormatter(), Path.Combine(LogDataPath, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log")).WriteTo
                .Sink(new LogEventSink(LogEntries, new LogLevelTextFormatter())).CreateLogger();

            Log.Debug("Logger start-up complete");
            Log.Information("Application starting up");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Logger start-up failed");
        }
    }
}