using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using KitX.Dashboard.ViewModels;
using NodifyM.Avalonia.Controls;
using Serilog;

namespace KitX.Dashboard.Views;

/// <summary>
/// The Bench orchestration window (the "workbench" — the design surface). Hosts the
/// editable NodifyM canvas over the in-memory ToolKit config, the palette, and the
/// property inspector. Delete key removes the selected node(s).
/// </summary>
public partial class BenchWindow : Window
{
    private readonly BenchViewModel viewModel = App.GetService<BenchViewModel>();

    public BenchWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
        KeyDown += OnWindowKeyDown;
        Closing += OnWindowClosing;
        // D11: the VM is transient per window and subscribes to the singleton event bus —
        // dispose it when the workbench actually closes. Also release the named
        // LocateRequested handler here so the window can't be retained via the canvas.
        Closed += (_, _) =>
        {
            viewModel.Canvas?.LocateRequested -= OnLocateRequested;
            viewModel.Dispose();
        };
        if (viewModel.Canvas is { } locateCanvas)
        {
            locateCanvas.LocateRequested += OnLocateRequested;
        }

        // Diagnostics: once the editor is in the tree, log whether the canvas
        // bindings actually resolved (caught the blank-canvas regression once).
        AttachedToVisualTree += OnAttached;

        // Fallback for external (same-process, other page) mutation of the shared
        // Toolkit object: re-project the config onto the canvas whenever the window
        // is (re)activated, preserving node locations.
        Activated += (_, _) =>
        {
            var canvas = viewModel.Canvas;
            if (canvas is null)
                return;
            viewModel.RefreshMountState();
            canvas.Rebuild();
            Log.Information($"[BenchWindow] Rebuild on activated: nodes={canvas.Nodes.Count} mounted={viewModel.IsMounted}");
        };
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        // Reflection bindings settle on the dispatcher — re-check one frame later.
        => Avalonia.Threading.Dispatcher.UIThread.Post(VerifyCanvasBinding,
            Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>Brings the located node's container into view (LocateRequested handler).</summary>
    private void OnLocateRequested(BenchNodeVM node)
    {
        try
        {
            var index = viewModel.Canvas?.Nodes.IndexOf(node) ?? -1;
            if (index >= 0 && BenchEditor.ContainerFromIndex(index) is Control container)
                container.BringIntoView();
        }
        catch
        {
            // NodifyEditor container lookup is best-effort; selection already happened.
        }
    }

    private void VerifyCanvasBinding()
    {
        var editor = this.GetVisualDescendants().OfType<NodifyEditor>().FirstOrDefault();
        var canvas = viewModel.Canvas;
        if (editor is null || canvas is null)
        {
            Log.Warning($"[BenchWindow] Editor or canvas missing — editor:{editor is not null}, canvas:{canvas is not null}");
            return;
        }

        var items = editor.ItemsSource?.Cast<object>().Count() ?? 0;
        Log.Information(
            $"[BenchWindow] Attached — toolkit:{viewModel.ActiveToolkit?.Meta?.Name ?? "‹null›"} " +
            $"configNodes:{canvas.Nodes.Count} connections:{canvas.Connections.Count} " +
            $"editorItemsSource:{items} editorDC:{editor.DataContext?.GetType().Name ?? "‹null›"}");

        if (items != canvas.Nodes.Count)
            Log.Warning($"[BenchWindow] Canvas binding mismatch: editor shows {items} of {canvas.Nodes.Count} config nodes");
    }

    /// <summary>Dirty-close confirmation: save & close / discard / cancel (Bench UX v2 C20).</summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (viewModel.IsDirty is false)
            return;

        e.Cancel = true;
        var box = MessageBoxManager.GetMessageBoxStandard(
            "未保存的更改",
            "工作台有未保存的配置更改，是否保存？",
            ButtonEnum.YesNoCancel,
            MsBox.Avalonia.Enums.Icon.Warning);
        var result = await box.ShowWindowDialogAsync(this);
        if (result == ButtonResult.Cancel)
            return;

        if (result == ButtonResult.Yes)
        {
            var ok = viewModel.TrySave();
            if (!ok)
                return;
        }

        Closing -= OnWindowClosing;
        Close();
    }

    /// <summary>Clicking a connection selects it (completion edge inspector / binding jump).</summary>
    private void OnConnectionPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: BenchConnectionVM connection } && viewModel.Canvas is { } canvas)
            canvas.SelectConnectionCommand.Execute(connection);
    }

    /// <summary>Double-clicking a workflow node opens the v6 content editor (Bench UX v2 C5).</summary>
    private void OnNodeDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control { DataContext: BenchNodeVM node } && node.Kind == BenchCanvasViewModel.BenchNodeKind.Workflow
            && viewModel.Canvas is { } canvas)
        {
            var workflow = viewModel.Workflows.FirstOrDefault(w => w.Id == node.ConfigId);
            if (workflow is not null)
                canvas.EditWorkflowCommand.Execute(workflow);
        }
    }

    /// <summary>Header close button (dirty-close confirmation is handled by OnWindowClosing).</summary>
    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;
        if (IsTextInputFocused())
            return;

        viewModel.Canvas?.DeleteSelectedNodesCommand.Execute(null);
        e.Handled = true;
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager?.GetFocusedElement();
        return focused is TextBox or ComboBox or NumericUpDown;
    }
}
