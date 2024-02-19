using Avalonia.Controls;
using FluentAvalonia.Styling;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views.Pages.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KitX.Dashboard.Views;

public static class ViewInstances
{
    public static PluginsLaunchWindow? PluginsLaunchWindow { get; set; }

    public static ObservableCollection<PluginCard> PluginCards { get; set; } = [];

    public static ObservableCollection<DeviceCard> DeviceCards { get; set; } = [];

    internal static ObservableCollection<PluginLaunchCard> PluginLaunchCards { get; set; } = [];

    public static MainWindow? MainWindow { get; set; }

    public static FluentAvaloniaTheme? FluentAvaloniaTheme { get; set; }

    public static List<Window> Windows { get; set; } = [];

    public static void ShowWindow<T>(T window, Window? owner = null) where T : Window
    {
        EventService.OnExiting += window.Close;

        Windows.Add(window);

        if (owner is null)
            window.Show();
        else
            window.ShowDialog(owner);
    }
}
