using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using PackForge.Core.Helpers;
using PackForge.Core.Service;
using PackForge.Logging;
using PackForge.ViewModels;
using Serilog;

namespace PackForge.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        App.GlobalLog.LogEntries.CollectionChanged += (_, _) => LogScrollViewer?.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            Log.Information("Deleting temp repo...");
            _ = GitService.DeleteTempRepo();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to delete temp repo: {ex.Message}");
        }
        
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
        
        Log.Information("Application window closed");
        base.OnClosed(e);
    }
}