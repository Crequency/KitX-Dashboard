using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Workflow;
using KitX.Core.Event;
using KitX.Dashboard;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Plugin;

namespace KitX.Dashboard.Services;

/// <summary>
/// UI State Service - Manages shared UI state across ViewModels
/// </summary>
public static class UIStateService
{
    public static ObservableCollection<IDeviceCase> DeviceCases { get; set; } = [];

    public static ObservableCollection<IWorkflowCase> WorkflowCases { get; set; } = [];

    public static ObservableCollection<PluginInfo> PluginInfos { get; set; } = [];

    public static MainWindow? MainWindow { get; set; }

    public static PluginsLaunchWindow? PluginsLaunchWindow { get; set; }

    public static List<Window> Windows { get; set; } = [];

    /// <summary>
    /// Tracks open workflow editor windows by workflow ID.
    /// Key: workflowId, Value: editor window instance.
    /// Used to prevent opening duplicate editors for the same workflow.
    /// </summary>
    public static Dictionary<string, Window> WorkflowEditorWindows { get; set; } = [];

    public static void ShowWindow<T>(T window, Window? owner = null, bool showDialog = false, bool onlyOneInSameTime = false)
        where T : Window
    {
        if (onlyOneInSameTime && Windows.Any(x => x.Title?.Equals(window.Title) ?? window.Title is null))
            return;

        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.OnExiting, (s, e) => window.Close());

        Windows.Add(window);

        window.Closed += (_, _) => Windows.Remove(window);

        if (showDialog && owner is not null)
            window.ShowDialog(owner);
        else if (owner is null || owner.IsVisible == false)
            window.Show();
        else
            window.Show(owner);
    }
}
