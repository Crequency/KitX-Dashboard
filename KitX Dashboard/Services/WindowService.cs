using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using KitX.Core.Contract.Event;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Models;

namespace KitX.Dashboard.Services;

/// <summary>
/// Default <see cref="IWindowService"/> implementation — semantics lifted verbatim from the
/// retired static UI-state service (open/owner/modal/dedup/close-cleanup).
/// <para>
/// Constructor-injects nothing (no ViewModels) to avoid the DI cycle that manifests as a
/// frozen UI; the shared event service is resolved lazily via <c>App.GetService</c> exactly
/// as the original did.
/// </para>
/// </summary>
public class WindowService : IWindowService
{
    public MainWindow? MainWindow { get; set; }

    public PluginsLaunchWindow? PluginsLaunchWindow { get; set; }

    public PanelHostWindow? PanelHostWindow { get; set; }

    public Toolkit? BenchToolkit { get; set; }

    public List<Window> Windows { get; } = [];

    public Dictionary<string, Window> WorkflowEditorWindows { get; } = [];

    public Dictionary<string, Window> BenchWindows { get; } = [];

    /// <summary>
    /// Opens (or focuses) the Bench workbench window for a ToolKit. One window per ToolKit:
    /// re-opening an already-open ToolKit activates the existing window.
    /// </summary>
    public void OpenBenchWindow(Toolkit toolkit)
    {
        var id = toolkit.GetId();
        if (BenchWindows.TryGetValue(id, out var existing) && existing is { IsVisible: true })
        {
            Serilog.Log.Information($"[WindowService] OpenBenchWindow: reusing open window for '{toolkit.Meta?.Name}' ({id})");
            existing.Activate();
            return;
        }

        Serilog.Log.Information(
            $"[WindowService] OpenBenchWindow: new window for '{toolkit.Meta?.Name}' ({id}) — " +
            $"triggers:{toolkit.Triggers.Count} workflows:{toolkit.Workflows.Count} panel:{toolkit.UiPanel is not null}");
        BenchToolkit = toolkit;
        var window = new BenchWindow();
        BenchWindows[id] = window;
        window.Closed += (_, _) => BenchWindows.Remove(id);
        // Show the workbench as an independent top-level window: showing it with the
        // MainWindow as owner keeps it permanently above the dashboard and prevents the
        // user from raising the dashboard above the editor (Windows owned-window z-order).
        ShowWindow(window);
    }

    /// <summary>Shows (and activates) the singleton Panel host window, creating it lazily.</summary>
    public void ShowPanelHostWindow()
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

        // Workbench trial-run and other manual entries activate the panel host; auto
        // surface requests have already set ShowActivated=false on their code path.
        win.ShowActivated = true;
        ShowWindow(win, MainWindow);
        win.Activate();
    }

    /// <summary>
    /// Opens (or reuses) the workflow editor for a workflow id, loading the given file.
    /// Reusing an already-open, visible editor for the same id focuses it instead of
    /// duplicating (Bench UX v2 C5).
    /// </summary>
    public void ShowWorkflowEditorWindow(string workflowId, string filePath)
    {
        if (WorkflowEditorWindows.TryGetValue(workflowId, out var existing) && existing is { IsVisible: true })
        {
            existing.Activate();
            return;
        }

        var window = new WorkflowEditorWindowV6();
        RegisterWorkflowEditor(workflowId, window);
        window.Show();
        _ = window.LoadWorkflowFileAsync(filePath);
    }

    /// <summary>
    /// Testable seam shared by <see cref="ShowWorkflowEditorWindow"/>: registers a window
    /// under <paramref name="workflowId"/> and removes it from <see cref="WorkflowEditorWindows"/>
    /// when the window closes.
    /// </summary>
    internal void RegisterWorkflowEditor(string workflowId, Window window)
    {
        WorkflowEditorWindows[workflowId] = window;
        window.Closed += (_, _) => WorkflowEditorWindows.Remove(workflowId);
    }

    /// <summary>Shows a mermaid export window, dialog-mode when a Bench owner exists.</summary>
    public void ShowMermaidExportWindow(string text, string? ownerToolkitId)
    {
        var window = new MermaidExportWindow(text);
        var owner = ownerToolkitId is not null ? BenchWindows.GetValueOrDefault(ownerToolkitId) : null;
        if (owner is not null)
            window.ShowDialog(owner);
        else
            window.Show();
    }

    public void ShowWindow<T>(T window, Window? owner = null, bool showDialog = false, bool onlyOneInSameTime = false)
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

    public void ShowDialog<T>(T window, Window? owner = null)
        where T : Window
        => ShowWindow(window, owner, showDialog: true);

    /// <summary>Cross-window focus request routed to the Panel host's ViewModel (if shown).</summary>
    public void FocusPanelInstance(string instanceId, bool openPanel)
        => FocusPanelDataContext(PanelHostWindow?.DataContext, instanceId, openPanel);

    /// <summary>
    /// Testable routing seam: a focus request lands on whatever <see cref="PanelHostViewModel"/>
    /// is exposed as the given DataContext. Kept static so a headless test can drive the routing
    /// with a real <see cref="PanelHostViewModel"/> without constructing the <see cref="PanelHostWindow"/>
    /// (which resolves its own DataContext via the DI container).
    /// </summary>
    internal static void FocusPanelDataContext(object? dataContext, string instanceId, bool openPanel)
    {
        if (dataContext is PanelHostViewModel ph)
            ph.FocusInstance(instanceId, openPanel);
    }
}
