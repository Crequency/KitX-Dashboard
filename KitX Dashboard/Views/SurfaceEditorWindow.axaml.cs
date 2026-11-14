using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// The card orchestrator (implementation design P5): a fixed 390 × 845 grid for one ToolKit.
/// </summary>
/// <remarks>
/// <para>The window knows nothing about layouts — it hands the view model a ToolKit id and lets it
/// read and write. Everything the window does beyond that is chrome.</para>
///
/// <para><b>The drag lives here rather than in the view model.</b> Pointer capture, coordinate
/// translation and the pressed/moved/released pairing are things only a control can do; the view
/// model is handed a canvas x and decides what weight that means. Splitting it that way keeps the
/// arithmetic testable without a window, and keeps the window free of layout rules.</para>
/// </remarks>
public partial class SurfaceEditorWindow : Window
{
    private SurfaceBoundaryVM? _dragging;

    /// <summary>Set once the author has chosen to leave with findings, so the second close is not asked again.</summary>
    private bool _closeConfirmed;

    /// <summary>The clock that writes the card while the window is open, or null before it is shown.</summary>
    internal DispatcherTimer? AutoSaveClock { get; private set; }

    /// <summary>Creates the window. The view model must be supplied before showing.</summary>
    public SurfaceEditorWindow()
    {
        InitializeComponent();

        Closing += OnClosing;
    }

    /// <summary>Starts the write-behind clock: one automatic save every thirty seconds, only when dirty.</summary>
    /// <remarks>
    /// The clock lives here rather than in the view model because it is a property of the window being
    /// open — a timer inside the editor would outlive the screen it describes and would keep writing a
    /// document no one is looking at.
    /// </remarks>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is not SurfaceEditorViewModel editor)
            return;

        AutoSaveClock = new DispatcherTimer { Interval = editor.AutoSaveInterval };
        AutoSaveClock.Tick += (_, _) => editor.AutoSaveIfDirty();
        AutoSaveClock.Start();
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        AutoSaveClock?.Stop();
        AutoSaveClock = null;

        base.OnClosed(e);
    }

    /// <summary>Writes the card, asks the validator, and asks the author when the answer is not clean.</summary>
    /// <remarks>
    /// <para><b>Cancelled first, then closed again.</b> The dialog is modal and the close has to wait
    /// for it, so the first attempt is always cancelled and the confirmed close re-enters this handler
    /// with <see cref="_closeConfirmed"/> set.</para>
    ///
    /// <para><b>A failed write is not a question.</b> There is no "leave anyway" on offer when the file
    /// could not be written — closing would drop the author's work while they believe it was saved, so
    /// the window stays open with the reason on its status line.</para>
    /// </remarks>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not SurfaceEditorViewModel editor)
            return;

        var verdict = editor.PrepareToClose();

        if (verdict == SurfaceEditorViewModel.SurfaceCloseVerdict.Close)
            return;

        e.Cancel = true;

        if (verdict == SurfaceEditorViewModel.SurfaceCloseVerdict.WriteFailed)
            return;

        var findings = string.Join(Environment.NewLine, editor.Diagnostics.Select(d => d.Message));

        if (!await new SurfaceLeaveDialog(findings).ShowDialog<bool>(this))
            return;

        _closeConfirmed = true;
        Close();
    }

    /// <summary>Creates the window and loads a ToolKit's layout.</summary>
    /// <param name="viewModel">The editor's view model.</param>
    /// <param name="toolkitId">The ToolKit to edit.</param>
    public SurfaceEditorWindow(SurfaceEditorViewModel viewModel, string toolkitId) : this()
    {
        DataContext = viewModel;
        viewModel.Load(toolkitId);
    }

    private void OnBoundaryPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SurfaceBoundaryVM boundary })
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _dragging = boundary;

        // Captured on the grip so the pointer may leave the eight-pixel strip and the drag keeps
        // going. Without this the drag would end the moment the pointer outran the border it is
        // moving — which, since the border follows the pointer, is the second frame.
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnBoundaryMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || DataContext is not SurfaceEditorViewModel editor)
            return;

        if (SurfaceCanvasLayer is null)
            return;

        editor.DragBoundary(_dragging, e.GetPosition(SurfaceCanvasLayer).X);
        e.Handled = true;
    }

    private void OnBoundaryReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is null)
            return;

        _dragging = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>Selects the pressed cell and begins dragging it to another place on the card.</summary>
    /// <remarks>
    /// <para>Handled, so the press does not reach the canvas underneath and immediately clear what it
    /// just selected. The remove button inside the cell takes its own press first — that handler is on
    /// the button, below this one in the route.</para>
    ///
    /// <para><b>A press starts the drag; a release that never moved commits nothing.</b> Deciding by
    /// travelled distance instead would need a threshold, and every threshold is a number that is
    /// wrong on somebody's mouse. A drag with no movement is already a no-op in the view model, so the
    /// press can start one without asking whether it is going to become one.</para>
    /// </remarks>
    private void OnCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SurfaceElementVM cell })
            return;

        if (DataContext is not SurfaceEditorViewModel editor)
            return;

        editor.Select(cell);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            editor.BeginReorder(cell);

            // Captured on the cell, because the cell under the pointer stops being this one as soon
            // as the drag crosses into the next cell — and an uncaptured drag would end there, on the
            // first cell boundary it reached.
            e.Pointer.Capture((IInputElement?)sender);
        }

        e.Handled = true;
    }

    /// <summary>Moves the drop line to where the pointer is, without touching the document.</summary>
    private void OnCellPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not SurfaceEditorViewModel editor || !editor.IsReordering)
            return;

        if (SurfaceCanvasLayer is null)
            return;

        var point = e.GetPosition(SurfaceCanvasLayer);
        editor.ReorderTo(point.X, point.Y);
        e.Handled = true;
    }

    /// <summary>Drops the dragged cell where the pointer was released.</summary>
    private void OnCellPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not SurfaceEditorViewModel editor || !editor.IsReordering)
            return;

        editor.CommitReorder();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>Forgets a drag whose pointer capture was taken away rather than released.</summary>
    private void OnCellCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is SurfaceEditorViewModel editor)
            editor.CancelReorder();
    }

    /// <summary>Clears the inspector: a press on the canvas itself is a press on nothing.</summary>
    private void OnCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SurfaceEditorViewModel editor)
            editor.Select(null);
    }
}
