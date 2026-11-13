using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
/// children at absolute positions without giving them any size. The <c>ItemsControl</c> measured to
/// zero, so nothing inside it was arranged onto a surface. Arithmetic was right, nothing was painted,
/// and every test was green.</para>
///
/// <para><b><c>Bounds &gt; 0</c> proves Measure, not Arrange, and that distinction cost me a
/// test.</b> The first version of this file asserted that each cell's <c>Bounds.Width</c> was greater
/// than zero. Mutating the layer back to a <c>Canvas</c> left all five tests green, and a probe
/// explained why: an <c>ItemsControl</c> lays its own containers out from its <c>ItemsPanel</c>, so
/// the cell's <c>ContentPresenter</c> still reports 125×88 while the <c>ItemsControl</c> above it
/// reports 0×0. The cell was never the zero. A control can be measured and still be arranged onto
/// nothing, so both facts need asserting: the control has a size, <em>and</em> every ancestor between
/// it and the window has one too.</para>
///
/// <para><b>A test that stays green under the defect is a smoke test, and it has to say so.</b>
/// <see cref="The_canvas_layer_gets_a_size_from_its_parent"/> is one: the layer keeps its 388×819 even
/// in the broken window, because the layer's own size comes from the <c>Grid</c> above it rather than
/// from what it arranges. It is kept — a window that fails to open should fail loudly and early — but
/// it is named and commented as a smoke test so nobody counts it as protection.</para>
///
/// <para><b>What this file cannot see.</b> It asserts geometry after layout; it does not assert
/// colours, brushes or glyphs. Showing a window at all needs Skia with
/// <c>UseHeadlessDrawing = false</c>, because the default headless backend creates no glyph
/// typefaces and <c>Show()</c> throws before layout. That also means the window never ran in an
/// environment that creates glyphs until this file existed — which is why a missing theme key
/// (defect 8) could not have been found here either.</para>
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

    /// <summary>Whether a test in this file fails when the canvas defect is present.</summary>
    private enum Under { Catches, PassesAnyway }

    /// <summary>
    /// What each test in this file does under the canvas defect, measured — not guessed.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a table and not prose.</b> Three versions of this file's guard were written, and
    /// two of them passed while the canvas was broken. That fact lived in comments, and comments are
    /// what the next person skims past to copy whichever test looks most like a template. The
    /// unclassified test gets copied; the one labelled "this one is wrong" does not, because the
    /// label is a sentence rather than a row.</para>
    ///
    /// <para><b>How each row was measured.</b> Change <c>SurfaceCanvasLayer</c> from a <c>Grid</c> to
    /// a <c>Canvas</c>, rebuild, run only this class, and read which tests failed. Restore. A row
    /// whose verdict was reasoned out rather than observed would defeat the point of the table.</para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, (Under Verdict, string UnderTheDefect)> Ledger =
        new Dictionary<string, (Under, string)>(StringComparer.Ordinal)
        {
            [nameof(The_canvas_layer_gets_a_size_from_its_parent)] =
                (Under.PassesAnyway, "layer still measures 388×819 — its size comes from the Grid row it sits in"),

            [nameof(Every_cell_is_measured_and_placed_inside_the_canvas)] =
                (Under.PassesAnyway, "cell still measures 125×88 — an ItemsControl lays its own containers out"),

            [nameof(The_two_cells_do_not_overlap_and_leave_the_declared_gap)] =
                (Under.PassesAnyway, "arithmetic on the view model, unaffected by where anything is drawn"),

            [nameof(A_row_of_one_weight_cell_draws_the_full_width)] =
                (Under.PassesAnyway, "same — it reads a width, not a rendering"),

            [nameof(The_items_controls_inside_the_layer_are_not_measured_to_zero)] =
                (Under.Catches, "the ItemsControl inside the layer reads 0×0"),

            [nameof(Every_ancestor_between_a_cell_and_the_window_has_a_size)] =
                (Under.Catches, "the Canvas ancestor reads 0×0"),
        };

    /// <summary>
    /// Every test in this file is classified in <see cref="Ledger"/>, so a new one cannot skip it.
    /// </summary>
    /// <remarks>
    /// This is the only test here whose subject is the other tests. It exists because the ledger is
    /// worthless the moment someone adds a seventh test and does not add a row: the table would
    /// still read as complete while covering five sixths of the file.
    /// </remarks>
    [AvaloniaFact]
    public void Every_test_in_this_file_says_whether_it_catches_the_canvas_defect()
    {
        var mine = nameof(Every_test_in_this_file_says_whether_it_catches_the_canvas_defect);

        var tests = typeof(SurfaceEditorRenderTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<AvaloniaFactAttribute>() is not null)
            .Select(method => method.Name)
            .Where(name => !string.Equals(name, mine, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Not empty: if reflection ever stops finding the attributes, the count goes from seven to
        // zero — a suite that shrinks by seven is visible on its own, without a cleverer assertion
        // here. The message is a convenience, not the safety net.
        Assert.NotEmpty(tests);

        var missing = tests.Where(name => !Ledger.ContainsKey(name)).ToList();
        Assert.True(
            missing.Count == 0,
            $"these tests say nothing about what they do under the canvas defect: " +
            $"{string.Join(", ", missing)}. Break the layer, run them, then add a row.");

        var stale = Ledger.Keys.Where(key => !tests.Contains(key, StringComparer.Ordinal)).ToList();
        Assert.True(
            stale.Count == 0,
            $"the ledger names tests that no longer exist: {string.Join(", ", stale)}");

        // Why ">= 1" and not a ratio or an absolute floor: having one catcher is the minimum for the
        // ledger to mean anything, not a sufficient one. Downgrading a second catcher to smoke would
        // leave this assertion green — it cannot see a slide from two to one. Coverage is read from
        // the Catches count itself, which is in this table where a reader will meet it. A threshold
        // that only guards one extreme does not guard the drift.
        var catching = Ledger.Count(row => row.Value.Verdict == Under.Catches);
        Assert.True(
            catching > 0,
            "no test in this file catches the canvas defect — the ledger is describing smoke only");
    }

    private static SurfaceEditorWindow Open(IReadOnlyList<SurfaceElement> elements)
    {
        var viewModel = new SurfaceEditorViewModel(new NoStore(elements), _ => []);
        var window = new SurfaceEditorWindow(viewModel, "render-test");

        window.Show();

        // Layout runs on the dispatcher queue rather than inside Show(). Without this the window is
        // open and every child still reports bounds of zero — which is the same reading a broken
        // layout gives, so the test would be unable to tell the two apart.
        //
        // That is the precondition for every test in this file, and it is a shape worth naming:
        // the fix was not another assertion, it was making the two states stop reading the same.
        // Anyone writing a second window test here and forgetting this line will see a tree of
        // zeroes and have no way to tell "layout has not run" from "layout is broken".
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

    /// <summary>
    /// <b>A smoke test, not a guard.</b> It stays green under the defect it looks like it covers.
    /// </summary>
    /// <remarks>
    /// The layer reports 388×819 even in the broken window, because its size comes from the
    /// <c>Grid</c> row it sits in — not from anything it arranges. Kept because a window that cannot
    /// open should fail loudly and early; named this way so nobody counts it as protection against
    /// the canvas defect. <see cref="An_arranged_cell_lands_inside_the_window"/> is the guard.
    /// </remarks>
    [AvaloniaFact]
    public void The_canvas_layer_gets_a_size_from_its_parent()
    {
        var window = Open(Pair());

        var layer = window.FindControl<Control>("SurfaceCanvasLayer");

        Assert.NotNull(layer);
        Assert.True(
            layer!.Bounds.Width > 0 && layer.Bounds.Height > 0,
            $"the canvas layer measured {layer.Bounds.Width}×{layer.Bounds.Height}; " +
            "a container inside a Canvas gets no size of its own");
    }

    /// <summary>
    /// Every control between a cell and the window has to have a size.
    /// </summary>
    /// <remarks>
    /// <para>This is what "arranged onto something" actually means here, and it took two wrong
    /// versions to get to. <c>TranslatePoint(…, window)</c> does <em>not</em> catch the defect: a
    /// cell under a zero-sized <c>ItemsControl</c> still translates to a point inside the client
    /// area, because the translation walks the same broken transforms. Nor does the cell's own
    /// <c>Bounds</c>: it stays 125×88.</para>
    ///
    /// <para>What does catch it is the ancestor chain. A control that is 388 pixels wide, sitting
    /// under a parent that is zero pixels wide, is not on the screen in any sense a reader would
    /// accept — and nothing in the cell's own numbers says so.</para>
    /// </remarks>
    [AvaloniaFact]
    public void Every_ancestor_between_a_cell_and_the_window_has_a_size()
    {
        var window = Open(Pair());

        var cells = Cells(window);
        Assert.Equal(2, cells.Count);

        foreach (var cell in cells)
        {
            for (Visual? ancestor = cell.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
            {
                Assert.True(
                    ancestor.Bounds.Width > 0 && ancestor.Bounds.Height > 0,
                    $"{ancestor.GetType().Name} between a cell and the window measured " +
                    $"{ancestor.Bounds.Width}×{ancestor.Bounds.Height} — the cell is inside " +
                    "something that occupies nothing, so it is drawn nowhere");
            }
        }
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
