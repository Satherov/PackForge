using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using DynamicData.Binding;
using PackForge.Core.Service;
using PackForge.Core.Terminal;
using PackForge.Core.Util;
using PackForge.ViewModels;
using Serilog;

namespace PackForge.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MainWindowViewModel.FilteredLogEntries.ObserveCollectionChanges().Subscribe(_ => { Dispatcher.UIThread.Post(() => { LogScrollViewer.ScrollToEnd(); }); });
        SizeChanged += OnSizeChanged;
    }

    private int _index;
    
    private void Terminal_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        
        switch (e.Key)
        {
            case Key.Enter:
            {
                string? input = textBox.Text;
                textBox.Clear();
                if (DataContext is MainWindowViewModel) MainWindowViewModel.HandleTerminalInput(input);
                _index = Terminal.CommandHistory.Count;
                break;
                
            }
            case Key.Up:
            {
                if(_index <= 0) return;
                
                _index--;
                Log.Debug($"Command history index: {_index} [{Terminal.CommandHistory[_index]}]");
                textBox.Text = Terminal.CommandHistory[_index];
                break;
            }
            case Key.Down:
            {
                if(_index >= Terminal.CommandHistory.Count - 1) return;
                
                _index++;
                Log.Debug($"Command history index: {_index} [{Terminal.CommandHistory[_index]}]");
                textBox.Text = Terminal.CommandHistory[_index];
                break;
            }
        }
    }
    
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Screen? screen = Screens.Primary;
        if (screen == null)
        {
            Log.Error("No primary screen found");
            return;
        }
        
        double scale = screen.Scaling;
        PixelRect workingArea = screen.WorkingArea;
        
        double desiredWidth = (workingArea.Width / scale) * 0.7;
        double desiredHeight = (workingArea.Height / scale) * 0.7;
        Width = desiredWidth;
        Height = desiredHeight;

        double x = workingArea.X + ((workingArea.Width / scale) - desiredWidth) / 2;
        double y = workingArea.Y + ((workingArea.Height / scale) - desiredHeight) / 2;

        Position = new PixelPoint((int)x, (int)y);
        
        Log.Debug($"Screen working area: {screen.WorkingArea.Width}x{screen.WorkingArea.Height}");
        Log.Debug($"Window opened with size: {Width}x{Height}");
    }

    protected override void OnClosed(EventArgs e)
    {
        Log.Information("Saving application state...");

        switch (DataContext)
        {
            case MainWindowViewModel vm:
                vm.SaveData();
                Log.Information("Application state saved");
                break;
            default:
                Log.Error("Failed to save application state. Invalid DataContext");
                break;
        }

        WindowHelper.CloseAllWindows();
        MainWindowViewModel.Shutdown();

        Log.Debug("Application window closed");
        base.OnClosed(e);
    }

    private const double AspectRatio = 16.0 / 9.0;
    private bool _isUpdating;

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        switch (_isUpdating)
        {
            case true:
                return;
        }
        _isUpdating = true;
        Height = Width / AspectRatio;
        _isUpdating = false;
    }
}