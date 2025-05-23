using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
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
            LogSystemInfo();
            Log.Information("Application starting up");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Logger start-up failed");
        }
    }
    
    private static void LogSystemInfo()
    {
        // Basic environment
        Log.Debug("Machine Name:          {Machine}",       Environment.MachineName);
        Log.Debug("Current User:          {User}",          Environment.UserName);
        Log.Debug("OS Description:        {OSDesc}",         RuntimeInformation.OSDescription);
        Log.Debug("OS Architecture:       {OSArch}",         RuntimeInformation.OSArchitecture);
        Log.Debug(".NET Runtime:          {FW}",             RuntimeInformation.FrameworkDescription);
        Log.Debug("Process Architecture:  {ProcArch}",       RuntimeInformation.ProcessArchitecture);

        // CPU & process
        Log.Debug("Logical CPU Count:     {CPU}",            Environment.ProcessorCount);
        Process proc = Process.GetCurrentProcess();
        Log.Debug("Process WorkingSet:    {WS}MB",            proc.WorkingSet64 / (1024 * 1024));
        Log.Debug("Process Threads:       {Threads}",        proc.Threads.Count);

        // GC heap info
        GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();
        Log.Debug("GC Heap TotalAvail:    {AvailMB}MB",       gcInfo.TotalAvailableMemoryBytes / (1024 * 1024));
        Log.Debug("GC Heap HighMemoryLoad:{HighPct}%",        gcInfo.HighMemoryLoadThresholdBytes * 100 / gcInfo.TotalAvailableMemoryBytes);

        // Drives
        foreach (DriveInfo d in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            Log.Debug("Drive {Name}: Type={Type}, {Total}GB total, {Free}GB free",
                d.Name,
                d.DriveType,
                d.TotalSize            / (1024L * 1024 * 1024),
                d.AvailableFreeSpace   / (1024L * 1024 * 1024));
        }

        // Network adapters
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()
                                                 .Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                Log.Debug("NIC {Name}: Type={Type}, Speed={Speed}Mbps",
                    nic.Name,
                    nic.NetworkInterfaceType,
                    nic.Speed / 1_000_000);
            }
        }
        catch (Exception)
        {
            Log.Warning("Failed to enumerate network interfaces");
        }
    }
}