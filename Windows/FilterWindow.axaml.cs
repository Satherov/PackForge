using System;
using Avalonia.Controls;
using Avalonia.Input;
using PackForge.Core.Data;
using PackForge.ViewModels;
using Serilog;

namespace PackForge.Windows;

public partial class FilterWindow : Window
{
    public FilterWindow()
    {
        InitializeComponent();
        KeyDown += MainWindow_KeyDown;
    }

    protected override void OnClosed(EventArgs e)
    {
        Log.Debug("Saving config state...");

        if (DataContext is FilterWindowViewModel vm)
        {
            vm.SaveData();
            Log.Debug("Config state saved");
        }
        else
        {
            Log.Error("Failed to save config state. Invalid DataContext");
        }

        Log.Debug("Config window closed");
        base.OnClosed(e);
    }
    
    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}