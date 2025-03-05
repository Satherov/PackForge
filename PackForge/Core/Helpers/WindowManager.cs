using System.Threading.Tasks;
using Avalonia.Threading;
using PackForge.ViewModels;
using PackForge.Windows;

namespace PackForge.Core.Util;

public static class WindowManager
{
    public static DispatcherOperation ShowConfigWindow()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var configWindow = new ConfigWindow
            {
                DataContext = new ConfigWindowViewModel()
            };
            configWindow.Show();
        });
    }

    public static DispatcherOperation ShowTokenWindow()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var tokenWindow = new TokenWindow
            {
                DataContext = new TokenWindowViewModel()
            };
            tokenWindow.Show();
        });
    }
}