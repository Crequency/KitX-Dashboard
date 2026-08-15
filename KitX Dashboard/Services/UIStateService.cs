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

    /// <summary>
    /// The Panel host window (ToolKit instance use surface), opened from the Tray / hotkey.
    /// Singleton so toggling shows/hides the same window.
    /// </summary>
    public static PanelHostWindow? PanelHostWindow { get; set; }

    /// <summary>The ToolKit the Bench design window is currently showing (set before opening).</summary>
    public static Toolkit? BenchToolkit { get; set; }

    public static List<Window> Windows { get; set; } = [];

    /// <summary>
    /// Tracks open workflow editor windows by workflow ID.
    /// Key: workflowId, Value: editor window instance.
    /// Used to prevent opening duplicate editors for the same workflow.
    /// </summary>
    public static Dictionary<string, Window> WorkflowEditorWindows { get; set; } = [];

    /// <summary>
    /// Tracks open Bench (workbench) windows by ToolKit id — one workbench per ToolKit,
    /// so opening an already-open ToolKit focuses its existing window instead of duplicating.
    /// </summary>
    public static Dictionary<string, Window> BenchWindows { get; set; } = [];

    /// <summary>
    /// Opens (or focuses) the Bench workbench window for a ToolKit. One window per ToolKit:
    /// re-opening an already-open ToolKit activates the existing window.
    /// </summary>
    public static void OpenBenchWindow(Toolkit toolkit)
    {
        var id = toolkit.GetId();
        if (BenchWindows.TryGetValue(id, out var existing) && existing is { IsVisible: true })
        {
            Serilog.Log.Information($"[UIStateService] OpenBenchWindow: reusing open window for '{toolkit.Meta?.Name}' ({id})");
            existing.Activate();
            return;
        }

        Serilog.Log.Information(
            $"[UIStateService] OpenBenchWindow: new window for '{toolkit.Meta?.Name}' ({id}) — " +
            $"triggers:{toolkit.Triggers.Count} workflows:{toolkit.Workflows.Count} panel:{toolkit.UiPanel is not null}");
        BenchToolkit = toolkit;
        var window = new Views.BenchWindow();
        BenchWindows[id] = window;
        window.Closed += (_, _) => BenchWindows.Remove(id);
        ShowWindow(window, MainWindow);
    }

    /// <summary>Shows (and activates) the singleton Panel host window, creating it lazily.</summary>
    public static void ShowPanelHostWindow()
    {
        PanelHostWindow ??= new PanelHostWindow();
        var win = PanelHostWindow;
        if (win.IsVisible)
        {
            if (win.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;
            win.Activate();
            return;
        }

        ShowWindow(win, MainWindow);
    }

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
