using Avalonia.Controls;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Plugin;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KitX.Dashboard.Views;

public static class ViewInstances
{
    public static ObservableCollection<DeviceCase> DeviceCases { get; set; } = [];

    public static ObservableCollection<PluginInfo> PluginInfos { get; set; } = [];

    public static MainWindow? MainWindow { get; set; }

    public static PluginsLaunchWindow? PluginsLaunchWindow { get; set; }

    public static List<Window> Windows { get; set; } = [];

    public static void ShowWindow<T>(T window, Window? owner = null, bool showDialog = false) where T : Window
    {
        EventService.OnExiting += window.Close;

        Windows.Add(window);

        if (showDialog && owner is not null)
            window.ShowDialog(owner);
        else if (owner is null)
            window.Show();
        else
            window.Show(owner);
    }
}
