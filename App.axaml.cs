using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PackForge.Logger;
using PackForge.ViewModels;
using PackForge.Windows;

namespace PackForge;

public class App : Application
{
    public static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PackForge"
    );

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
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
}