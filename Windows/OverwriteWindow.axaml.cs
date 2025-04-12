using System;
using Avalonia.Controls;
using PackForge.ViewModels;
using Serilog;

namespace PackForge.Windows;

public partial class OverwriteWindow : Window
{
    public OverwriteWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        Log.Information("Saving overwrite state...");

        if (DataContext is OverwriteWindowViewModel vm)
        {
            vm.SaveData();
            Log.Information("Overwrite state saved");
        }
        else
        {
            Log.Error("Failed to save overwrite state. Invalid DataContext");
        }

        Log.Debug("Overwrite window closed");
        base.OnClosed(e);
    }
}