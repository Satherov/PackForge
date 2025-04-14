using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DynamicData.Binding;
using PackForge.Core.Service;
using PackForge.Core.Util;
using PackForge.ViewModels;
using Serilog;

namespace PackForge.Windows;

public partial class MainWindow : Window
{
    public double WindowWidth = 1280.0;
    public double WindowHeight = 720.0;

    public MainWindow()
    {
        InitializeComponent();
        MainWindowViewModel.FilteredLogEntries.ObserveCollectionChanges().Subscribe(_ => { Dispatcher.UIThread.Post(() => { LogScrollViewer.ScrollToEnd(); }); });
        SizeChanged += OnSizeChanged;
    }

    private void Terminal_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox) return;

        string? input = textBox.Text;
        textBox.Clear();

        if (DataContext is MainWindowViewModel) MainWindowViewModel.HandleTerminalInput(input);
    }

    protected override void OnClosed(EventArgs e)
    {
        Log.Information("Saving application state...");

        if (DataContext is MainWindowViewModel vm)
        {
            vm.SaveData();
            Log.Information("Application state saved");
        }
        else
        {
            Log.Error("Failed to save application state. Invalid DataContext");
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
        if (_isUpdating)
            return;
        _isUpdating = true;
        Height = Width / AspectRatio;
        _isUpdating = false;
    }
}