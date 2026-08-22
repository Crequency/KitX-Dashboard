using System.Collections.Generic;
using Avalonia.Controls;
using KitX.Dashboard.Views;
using KitX.ToolKit.Models;

namespace KitX.Dashboard.Services;

/// <summary>
/// Window orchestration service — the DI replacement for the retired static UI-state
/// window coordinator. Owns the app's tracked window instances and the window-to-window
/// coordination that used to reach across ViewModels (e.g. routing a focus request to the
/// Panel host's DataContext).
/// <para>
/// The service is a <b>window coordinator only</b>: it never constructor-injects a
/// ViewModel (that would create a DI cycle that manifests as a frozen UI). Any ViewModel
/// interaction is resolved lazily — either via <c>App.GetService</c> or through a window's
/// <c>DataContext</c>.
/// </para>
/// </summary>
public interface IWindowService
{
    /// <summary>The main dashboard window instance (self-registered by <see cref="MainWindow"/>).</summary>
    MainWindow? MainWindow { get; set; }

    /// <summary>The persistent plugin-launcher window.</summary>
    PluginsLaunchWindow? PluginsLaunchWindow { get; set; }

    /// <summary>
    /// The Panel host window (ToolKit instance use surface). Singleton so toggling
    /// shows/hides the same window.
    /// </summary>
    PanelHostWindow? PanelHostWindow { get; set; }

    /// <summary>The ToolKit the Bench design window is currently showing (set before opening).</summary>
    Toolkit? BenchToolkit { get; set; }

    /// <summary>All windows opened through <see cref="ShowWindow{T}"/> (for dedup/cleanup).</summary>
    List<Window> Windows { get; }

    /// <summary>Tracks open workflow editor windows by workflow ID (dedup + Closed removal).</summary>
    Dictionary<string, Window> WorkflowEditorWindows { get; }

    /// <summary>Tracks open Bench workbench windows by ToolKit id (one workbench per ToolKit).</summary>
    Dictionary<string, Window> BenchWindows { get; }

    /// <summary>Opens (or focuses) the Bench workbench window for a ToolKit.</summary>
    void OpenBenchWindow(Toolkit toolkit);

    /// <summary>Shows (and activates) the singleton Panel host window, creating it lazily.</summary>
    void ShowPanelHostWindow();

    /// <summary>
    /// Opens (or reuses) the workflow editor for a workflow id, loading the given file.
    /// </summary>
    void ShowWorkflowEditorWindow(string workflowId, string filePath);

    /// <summary>Shows a mermaid export window, dialog-mode when a Bench owner exists.</summary>
    void ShowMermaidExportWindow(string text, string? ownerToolkitId);

    /// <summary>
    /// Opens a window through the shared open/close lifecycle (registers in <see cref="Windows"/>,
    /// unsubscribes OnExiting on close, dedups when requested).
    /// </summary>
    void ShowWindow<T>(T window, Window? owner = null, bool showDialog = false, bool onlyOneInSameTime = false)
        where T : Window;

    /// <summary>Opens a window as a modal dialog with the given owner.</summary>
    void ShowDialog<T>(T window, Window? owner = null) where T : Window;

    /// <summary>Cross-window focus request routed to the Panel host's ViewModel (if shown).</summary>
    void FocusPanelInstance(string instanceId, bool openPanel);
}
