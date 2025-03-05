using System;

namespace PackForge.Logging;

public class LogEntry(DateTime timestamp, string level, string message)
{
    public DateTime Timestamp { get; } = timestamp;
    public string Level { get; } = level;
    public string Message { get; } = message;
}