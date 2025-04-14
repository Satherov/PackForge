using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace PackForge.Windows;

public partial class TokenWindow : Window
{
    public TokenWindow()
    {
        InitializeComponent();
        this.KeyDown += MainWindow_KeyDown;
    }
    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}