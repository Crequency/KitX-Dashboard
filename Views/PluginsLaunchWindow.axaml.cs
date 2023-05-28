using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KitX_Dashboard.ViewModels;
using Serilog;
using System;

namespace KitX_Dashboard.Views;

public partial class PluginsLaunchWindow : Window
{
    private readonly PluginsLaunchWindowViewModel viewModel = new();

    public PluginsLaunchWindow()
    {
        var location = $"{nameof(PluginsLaunchWindow)}.ctor";

        InitializeComponent();

        DataContext = viewModel;

        if (OperatingSystem.IsWindows() == false)
        {
            try
            {
                Background = Resources["ThemePrimaryAccent"] as SolidColorBrush;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"In {location}: {ex.Message}");
            }
        }
    }

    private void PluginsLaunchWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        => BeginMoveDrag(e);

    private void PluginsLaunchWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
