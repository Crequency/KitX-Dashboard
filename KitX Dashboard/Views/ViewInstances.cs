using FluentAvalonia.Styling;
using KitX.Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;

namespace KitX.Dashboard.Views;

internal static class ViewInstances
{
    internal static PluginsLaunchWindow? PluginsLaunchWindow { get; set; }

    internal static ObservableCollection<PluginCard> PluginCards { get; set; } = [];

    internal static ObservableCollection<PluginLaunchCard> PluginLaunchCards { get; set; } = [];

    internal static ObservableCollection<DeviceCard> DeviceCards { get; set; } = [];

    internal static MainWindow? MainWindow { get; set; }

    internal static FluentAvaloniaTheme? FluentAvaloniaTheme { get; set; }
}
