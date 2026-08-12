using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Plugin;
using KitX.ToolKit.Models;

namespace KitX.Dashboard.Services;

/// <summary>
/// UI State Service - Manages shared UI state across ViewModels
/// </summary>
public static class UIStateService
{
    public static ObservableCollection<IDeviceCase> DeviceCases { get; set; } = [];

    public static ObservableCollection<IWorkflowCase> WorkflowCases { get; set; } = [];

    public static ObservableCollection<PluginInfo> PluginInfos { get; set; } = [];

    /// <summary>
    /// ToolKits loaded for the ToolKit management page (future replacement for the
    /// workflow management page). Populated by <see cref="Pages.ToolkitPageViewModel"/>.
    /// </summary>
    public static ObservableCollection<Toolkit> Toolkits { get; set; } = [];

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

        // D5: the singleton EventService must not keep a reference to closed windows —
        // unsubscribe from OnExiting when the window closes.
        EventHandler<EventArgs> onExitingHandler = (s, e) => window.Close();
        eventService.Subscribe(EventNames.OnExiting, onExitingHandler);

        Windows.Add(window);

        window.Closed += (_, _) =>
        {
            Windows.Remove(window);
            eventService.Unsubscribe(EventNames.OnExiting, onExitingHandler);
        };

        if (showDialog && owner is not null)
            window.ShowDialog(owner);
        else if (owner is null || owner.IsVisible == false)
            window.Show();
        else
            window.Show(owner);
    }
}
