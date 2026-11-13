using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

    /// <summary>Creates the window. The view model must be supplied before showing.</summary>
    public SurfaceEditorWindow()
    {
        InitializeComponent();
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
}
