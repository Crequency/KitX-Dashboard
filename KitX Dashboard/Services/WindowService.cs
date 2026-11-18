using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using KitX.Core.Contract.Event;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract.Declaration;

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
            $"triggers:{toolkit.Triggers.Count} workflows:{toolkit.Workflows.Count} keys:{toolkit.Keys.Count}");
        BenchToolkit = toolkit;
        var window = new BenchWindow();
        BenchWindows[id] = window;
        window.Closed += (_, _) => BenchWindows.Remove(id);
        // Show the workbench as an independent top-level window: showing it with the
        // MainWindow as owner keeps it permanently above the dashboard and prevents the
        // user from raising the dashboard above the editor (Windows owned-window z-order).
        ShowWindow(window);
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

    /// <summary>Tracks open card orchestrators by ToolKit id (one per ToolKit).</summary>
    public Dictionary<string, Window> SurfaceEditorWindows { get; } = [];

    /// <summary>
    /// Opens (or focuses) the card orchestrator for a ToolKit.
    /// </summary>
    /// <remarks>
    /// The view model is resolved lazily through <see cref="App.GetService{T}"/>, for the same
    /// reason every other window here does it: constructor-injecting a ViewModel into this service
    /// produces the DI cycle that manifests as a frozen UI.
    /// </remarks>
    public void OpenSurfaceEditorWindow(string toolkitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitId);

        if (SurfaceEditorWindows.TryGetValue(toolkitId, out var existing) && existing is { IsVisible: true })
        {
            existing.Activate();
            return;
        }

        var viewModel = App.GetService<SurfaceEditorViewModel>();
        var window = new SurfaceEditorWindow(viewModel, toolkitId);

        SurfaceEditorWindows[toolkitId] = window;
        window.Closed += (_, _) => SurfaceEditorWindows.Remove(toolkitId);

        ShowWindow(window);
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

    /// <inheritdoc/>
    /// <remarks>
    /// <para>Uses <c>ShowDialog&lt;bool&gt;</c> directly rather than <see cref="ShowDialog{T}"/>:
    /// the non-generic wrapper calls <c>ShowDialog(owner)</c> without a type argument, which
    /// returns a bare <see cref="Task"/> and throws the answer away.</para>
    /// <para>A modal with no owner is not modal in Avalonia — it would take the whole desktop and
    /// leave the asker unblocked. The page can be built without a main window in tests, so the
    /// null case answers "no" instead of throwing.</para>
    /// </remarks>
    public async Task<bool> ConfirmDeleteToolkitAsync(string toolkitName, Window? owner = null)
    {
        if (owner is null)
            return false;

        var dialog = new DeleteToolkitDialog(toolkitName);

        // Anything but an explicit confirm means no: closing with the X and pressing Escape both
        // settle as null on the task, and `Close(false)` settles as false.
        return await dialog.ShowDialog<bool>(owner);
    }

    /// <summary>
    /// Asks whether to take every element off the card, saying how many there are.
    /// </summary>
    /// <param name="elementCount">How many elements the canvas holds right now.</param>
    /// <param name="owner">The window the question belongs to.</param>
    /// <returns><see langword="true"/> only when the reader confirmed.</returns>
    /// <remarks>
    /// <para>Same shape and same reasoning as <see cref="ConfirmDeleteToolkitAsync"/>: the caller is
    /// a view model, and a view model that builds a <c>Window</c> cannot be tested without a running
    /// application. A modal with no owner answers "no" rather than taking the desktop.</para>
    /// <para>The count is a parameter rather than something the dialog reads, because the dialog has
    /// no view model and the number lives on the canvas — asking the caller is the only place the
    /// number is known, and the sentence is useless without it.</para>
    /// </remarks>
    public async Task<bool> ConfirmClearSurfaceAsync(int elementCount, Window? owner = null)
    {
        if (owner is null)
            return false;

        var dialog = new ClearSurfaceDialog(elementCount);

        return await dialog.ShowDialog<bool>(owner);
    }

}
