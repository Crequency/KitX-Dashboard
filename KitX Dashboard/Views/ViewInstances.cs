using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Plugin;

namespace KitX.Dashboard.Views;

public static class ViewInstances
{
    public static ObservableCollection<DeviceCase> DeviceCases { get; set; } = [];

    public static ObservableCollection<PluginInfo> PluginInfos { get; set; } = [];

    public static MainWindow? MainWindow { get; set; }

    public static PluginsLaunchWindow? PluginsLaunchWindow { get; set; }

    public static List<Window> Windows { get; set; } = [];

    public static void ShowWindow<T>(T window, Window? owner = null, bool showDialog = false, bool onlyOneInSameTime = false) where T : Window
    {
        if (onlyOneInSameTime && Windows.Any(x => x.Title?.Equals(window.Title) ?? window.Title is null))
            return;

        EventService.OnExiting += window.Close;

        Windows.Add(window);

        if (showDialog && owner is not null)
            window.ShowDialog(owner);
        else if (owner is null || owner.IsVisible == false)
            window.Show();
        else
            window.Show(owner);
    }
}
