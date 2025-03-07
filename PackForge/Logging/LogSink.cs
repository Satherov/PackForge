using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace PackForge.Logging;

public class LogSink(ObservableCollection<LogEntry> logEntries, ITextFormatter formatter) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);
        var rawMessage = writer.ToString();

        var entry = new LogEntry(
            logEvent.Timestamp.DateTime,
            logEvent.Level.ToString(),
            rawMessage.ReplaceLineEndings().Trim()
        );

        Dispatcher.UIThread.Post(() =>
        {
            if (logEntries.Count > 10000) logEntries.RemoveAt(0);
            logEntries.Add(entry);
        });
    }
}