using System.IO;
using Serilog.Events;
using Serilog.Formatting;

namespace PackForge.Logger;

public class LogLevelTextFormatter : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        string? levelString = logEvent.Level switch
        {
            LogEventLevel.Fatal => "FATAL",
            LogEventLevel.Error => "ERROR",
            LogEventLevel.Warning => "WARN",
            LogEventLevel.Information => "INFO",
            LogEventLevel.Debug => "DEBUG",
            LogEventLevel.Verbose => "VERBOSE",

            _ => logEvent.Level.ToString().ToUpper()
        };

        output.Write($"[{logEvent.Timestamp:yyyy-MM-dd HH:mm:ss:fff}]");
        output.Write(' ');
        output.Write($"[{levelString}]");
        output.Write(' ');
        output.Write(logEvent.RenderMessage());
        output.WriteLine();

        if (logEvent.Exception != null) output.WriteLine(logEvent.Exception);
    }
}