using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PackForge.Logging;
using PackForge.ViewModels;
using PackForge.Windows;
using Serilog;

namespace PackForge;

public class App : Application
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PackForge", "logs"
    );

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime) return;
        GlobalLog.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };

        base.OnFrameworkInitializationCompleted();
    }

    public static class GlobalLog
    {
        public static ObservableCollection<LogEntry> LogEntries { get; } = [];

        public static void Initialize()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(new LogLevelTextFormatter(),
                    Path.Combine(AppDataPath, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log"))
                .WriteTo.Sink(new AvaloniaLogSink(LogEntries, new LogLevelTextFormatter()))
                .CreateLogger();

            try
            {
                Log.Information("Application starting up");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application start-up failed");
            }
        }
    }
}