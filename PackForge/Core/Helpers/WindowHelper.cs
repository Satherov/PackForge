using Avalonia.Controls;
using Avalonia.Threading;
using PackForge.ViewModels;
using PackForge.Windows;
using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace PackForge.Core.Helpers;

public static class WindowHelper
{
    private static ConfigWindow? _configWindow;
    private static OverwriteWindow? _overwriteWindow;
    private static TokenWindow? _tokenWindow;

    public static ConfigWindow ConfigWindow
    {
        get
        {
            if (_configWindow is { IsVisible: true }) return _configWindow;
            
            _configWindow = new ConfigWindow
            {
                DataContext = new ConfigWindowViewModel()
            };
            _configWindow.Closed += (_, _) => _configWindow = null;
            return _configWindow;
        }
    }

    public static OverwriteWindow OverwriteWindow
    {
        get
        {
            if (_overwriteWindow is { IsVisible: true }) return _overwriteWindow;

            _overwriteWindow = new OverwriteWindow
            {
                DataContext = new OverwriteWindowViewModel()
            };
            _overwriteWindow.Closed += (_, _) => _overwriteWindow = null;
            return _overwriteWindow;
        }
    }

    public static TokenWindow TokenWindow
    {
        get
        {
            if (_tokenWindow is { IsVisible: true }) return _tokenWindow;
            
            _tokenWindow = new TokenWindow
            {
                DataContext = new TokenWindowViewModel()
            };
            _tokenWindow.Closed += (_, _) => _tokenWindow = null;
            return _tokenWindow;
        }
    }

    public static DispatcherOperation ShowWindow(Func<Window> windowFactory)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = windowFactory();
            
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.IsEnabled = false;
                    window.Closed += (_, _) => mainWindow.IsEnabled = true;
                }
            }

            if (!window.IsVisible)
            {
                window.Show();
            }
            else
            {
                window.Activate();
                window.Focus();
            }
        });
    }
    
    public static DispatcherOperation FocusWindow(Func<Window> windowFactory)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = windowFactory();
            window.Activate();
            window.Focus();
        });
    }

    public static DispatcherOperation CloseAllWindows()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _configWindow?.Close();
            _overwriteWindow?.Close();
            _tokenWindow?.Close();

            _configWindow = null;
            _overwriteWindow = null;
            _tokenWindow = null;
        });
    }
}
