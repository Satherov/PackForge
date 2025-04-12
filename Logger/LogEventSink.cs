using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace PackForge.Logger;

public class LogEventSink(ObservableCollection<GlobalLog.LogEntry> logEntries, ITextFormatter formatter) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        using StringWriter writer = new();
        formatter.Format(logEvent, writer);
        string rawMessage = writer.ToString();

        GlobalLog.LogEntry entry = new(logEvent.Timestamp.DateTime, logEvent.Level, rawMessage.ReplaceLineEndings().Trim());

        Dispatcher.UIThread.Post(() =>
        {
            if (logEntries.Count > 10000) logEntries.RemoveAt(0);
            logEntries.Add(entry);
        });
    }
}