using System;
using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData.Binding;
using PackForge.Core.Service;
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
}