using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The editor's cells have to reach the screen, not only the view model.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> Nine geometry tests asserted <c>Left</c>, <c>Width</c> and
/// <c>Top</c> on the view model and passed while the window drew an empty canvas: the cells were
/// inside an <c>ItemsControl</c> whose parent was a <c>Canvas</c>, and a <c>Canvas</c> arranges its
/// children at absolute positions without giving them any size. Both measured to zero. Arithmetic
/// was right, nothing was painted, and every test was green.</para>
///
/// <para><b>What it costs and what it cannot do.</b> This is the only file in the project that shows
/// a real window, and headless Avalonia can do that here only because the editor's styles do not pull
/// in FluentAvalonia's icon font — the Surface card's do, and <c>Show()</c> on those throws
/// <c>Could not create glyphTypeface. Font family: Symbols</c>. So this asserts geometry after layout,
/// which is the layer where "measured to zero" lives; it does not assert colours or brushes.</para>
///
/// <para><b>The assertion that would have caught it.</b> Not "the element exists", and not "the
/// view model says 122" — but "after layout, this control's <c>Bounds.Width</c> is greater than
/// zero". A control that never got a size still exists in the logical tree and still has a correct
/// view model; only its bounds show that nobody measured it.</para>
/// </remarks>
public class SurfaceEditorRenderTests
{
    /// <summary>A store that hands back one document and writes nowhere.</summary>
    private sealed class NoStore : ISurfaceStore
    {
        private readonly IReadOnlyList<SurfaceElement> _elements;

        public NoStore(IReadOnlyList<SurfaceElement> elements) => _elements = elements;

        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = _elements }, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
        }

        public void Delete(string toolkitId)
        {
        }

        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }

    /// <summary>Two cells on one row: the shape that exposed the empty canvas.</summary>
    private static IReadOnlyList<SurfaceElement> Pair() =>
    [
        new SurfaceTextElement { Row = 1, Weight = 1, Key = "left" },
        new SurfaceButtonElement { Row = 1, Weight = 2, Label = "right", Events = ["go"] },
    ];

    private static SurfaceEditorWindow Open(IReadOnlyList<SurfaceElement> elements)
    {
        var viewModel = new SurfaceEditorViewModel(new NoStore(elements), _ => []);
        var window = new SurfaceEditorWindow(viewModel, "render-test");

        window.Show();

        // Layout runs on the dispatcher queue rather than inside Show(). Without this the window is
        // open and every child still reports bounds of zero — which is the same reading a broken
        // layout gives, so the test would be unable to tell the two apart.
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    /// <summary>The outermost control of each cell, found by the type of its view model.</summary>
    /// <remarks>
    /// DataContext is inherited, so every descendant inside a cell also reports the cell's
    /// <see cref="SurfaceElementVM"/>. Selecting the descendant whose parent is <em>not</em> a cell
    /// is what picks the template's root Border: the control the Canvas positions.
    /// </remarks>
    private static List<Control> Cells(Window window) =>
    [
        .. window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.DataContext is SurfaceElementVM)
            .Where(c => c.GetVisualParent() is not Control { DataContext: SurfaceElementVM })
    ];

    [AvaloniaFact]
    public void The_canvas_layer_gets_a_size_from_its_parent()
    {
        // The regression itself. The cells lived inside an ItemsControl that was a direct child of
        // a Canvas, so the ItemsControl measured to zero and so did everything below it. Asserting
        // the *layer* rather than a cell says why: it is the container that failed to be measured.
        var window = Open(Pair());

        var layer = window.FindControl<Control>("SurfaceCanvasLayer");

        Assert.NotNull(layer);
        Assert.True(
            layer!.Bounds.Width > 0 && layer.Bounds.Height > 0,
            $"the canvas layer measured {layer.Bounds.Width}×{layer.Bounds.Height}; " +
            "a container inside a Canvas gets no size of its own");
    }

    [AvaloniaFact]
    public void Every_cell_is_measured_and_placed_inside_the_canvas()
    {
        var window = Open(Pair());

        var cells = Cells(window);
        Assert.Equal(2, cells.Count);

        // The cell's root Border, one level below the ContentPresenter the ItemsControl generates.
        // Asserting the ContentPresenter (a superset of this list, since it also carries the cell's
        // DataContext) is exactly the mistake this file was written to stop: an ItemsControl inside
        // a Canvas is laid out by its own ItemsPanel, so its containers keep their natural size even
        // while the ItemsControl itself measures to zero. The cells were never the zero.
        foreach (var cell in cells)
        {
            Assert.True(
                cell.Bounds.Width > 0,
                $"a cell of kind {(cell.DataContext as SurfaceElementVM)?.Kind} has width " +
                $"{cell.Bounds.Width} — it was never measured");
            Assert.True(cell.Bounds.Height > 0, "a cell has no height");
        }
    }

    /// <summary>
    /// The layer both ItemsControls live in has to be measured, or neither is.
    /// </summary>
    /// <remarks>
    /// <b>This is the assertion that actually catches the defect.</b> With the layer as a
    /// <c>Canvas</c>, the cells still report their declared bounds — the probe reads
    /// <c>125×88</c> and <c>250×88</c> for the two of them — while the window draws an empty card.
    /// What collapses is the layer's children: each <c>ItemsControl</c> measured <c>0×0</c>, so
    /// nothing below it was ever arranged onto a surface. Asserting a cell's <c>Bounds</c> would
    /// pass against the broken window, which is why this test asks about the container instead.
    /// </remarks>
    [AvaloniaFact]
    public void The_items_controls_inside_the_layer_are_not_measured_to_zero()
    {
        var window = Open(Pair());

        var layer = window.FindControl<Control>("SurfaceCanvasLayer");
        Assert.NotNull(layer);

        // Only the two inside the layer: the palette on the left is an ItemsControl too, with the
        // same DataContext, and it is measured correctly — counting every ItemsControl in the window
        // would mix a working one into the assertion and could pass on its behalf.
        var controls = layer!.GetVisualDescendants()
            .OfType<ItemsControl>()
            .ToList();

        Assert.Equal(2, controls.Count);

        foreach (var control in controls)
        {
            Assert.True(
                control.Bounds.Width > 0 && control.Bounds.Height > 0,
                $"an ItemsControl measured {control.Bounds.Width}×{control.Bounds.Height}; " +
                "a Canvas gives its children absolute positions and no size, so everything inside " +
                "it is arranged onto nothing");
        }
    }

    [AvaloniaFact]
    public void The_two_cells_do_not_overlap_and_leave_the_declared_gap()
    {
        // Width > 0 alone would still pass if every cell were stacked at the origin. This is the
        // other half of "it was drawn": the cells sit where the weights say, and the gap between
        // them is the eight pixels the border is meant to live in.
        var window = Open(Pair());

        var cells = Cells(window)
            .OrderBy(c => c.Bounds.Left)
            .ToList();

        Assert.Equal(2, cells.Count);

        var left = cells[0].Bounds;
        var right = cells[1].Bounds;

        Assert.True(
            left.Right <= right.Left + 0.5,
            $"the left cell ends at {left.Right} and the right one begins at {right.Left}");
        Assert.Equal(SurfaceElementVM.CellGap, right.Left - left.Right, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_row_of_one_weight_cell_draws_the_full_width()
    {
        // The threshold case for "measured": a single cell has no sibling to share with, so it must
        // take the whole shareable width. If the canvas layer were collapsed this would be the test
        // that says the difference is not merely "some pixels".
        var window = Open([new SurfaceTextElement { Row = 1, Weight = 1, Key = "only" }]);

        var cell = Assert.Single(Cells(window));

        Assert.Equal(
            SurfaceElementVM.CanvasWidth - SurfaceElementVM.CellGap,
            cell.Bounds.Width,
            tolerance: 0.5);
    }
}
