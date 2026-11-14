using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Dragging a cell's body moves it within its row or onto another row, and the document is rewritten
/// only when the pointer is released.
/// </summary>
/// <remarks>
/// <para><b>What this file guards.</b> Before it, a cell could not be moved at all: the only way to
/// change where an element sat was to type a row number into the inspector, and the left-to-right
/// order inside a row could not be changed from the window at all. Since Q101 the array order
/// <em>is</em> the row order, so "this cell belongs to the left of that one" had no editor.</para>
///
/// <para><b>Why every assertion is about identity as well as order.</b> The obvious implementation of
/// a drop is "write the new declaration order, then rebuild the canvas". That implementation passes
/// every assertion about what the file says and still breaks the gesture: a rebuild replaces the cell
/// the pointer was dragging with a new object, so the last frame of the drag lands on nothing, the
/// selection is lost, and the inspector forgets what it was describing. The tests therefore pin the
/// same objects in the new order, not merely the same declarations.</para>
///
/// <para><b>What is measured here, and what is not.</b> These are view-model tests: they call the same
/// methods the window calls on pointer move and release. Whether the window wires those events to
/// those methods is checked once, by
/// <see cref="The_window_draws_the_drop_line_at_the_place_the_drag_names"/>; the pointer simulation
/// itself is not available in this host without a real mouse.</para>
/// </remarks>
public class SurfaceEditorDragTests
{
    /// <summary>The y of a drag aimed at the middle of one row's band.</summary>
    /// <remarks>
    /// From the same two constants the cells place themselves with, so a test that aims at row 2 aims
    /// at the pixels row 2 is drawn on rather than at a number that happens to work today.
    /// </remarks>
    private static double RowCentre(int row) =>
        SurfaceElementVM.RowOrigin + ((row - 1) * SurfaceElementVM.RowHeight) + (SurfaceElementVM.RowHeight / 2);

    /// <summary>The keys of the cells, in declaration order — which is also their left-to-right order.</summary>
    private static string[] Order(SurfaceEditorViewModel editor) =>
        [.. editor.Elements.Select(cell => cell.Key ?? cell.Label)];

    [Fact]
    public void A_cell_dragged_left_past_its_neighbours_midpoint_moves_one_place_left()
    {
        var editor = Editor(Document(3, 1));

        var middle = editor.Elements[1];

        editor.BeginReorder(middle);
        editor.ReorderTo(20, RowCentre(1));
        editor.CommitReorder();

        Assert.Equal(["k1", "k0", "k2"], Order(editor));

        // The row it stayed on is still one row: a within-row drag must not renumber anything.
        Assert.All(editor.Elements, cell => Assert.Equal(1, cell.Element.Row));
    }

    [Fact]
    public void A_cell_dragged_right_past_its_neighbours_midpoint_moves_one_place_right()
    {
        var editor = Editor(Document(3, 1));

        var middle = editor.Elements[1];

        editor.BeginReorder(middle);
        editor.ReorderTo(SurfaceElementVM.CanvasWidth - 20, RowCentre(1));
        editor.CommitReorder();

        Assert.Equal(["k0", "k2", "k1"], Order(editor));
        Assert.All(editor.Elements, cell => Assert.Equal(1, cell.Element.Row));
    }

    /// <summary>
    /// A cell dropped over a cell of another row lands before it or after it, by the same midpoint
    /// rule as a within-row drop.
    /// </summary>
    /// <remarks>
    /// Two assertions, not one, because the two halves fail differently: the first drop lands at index
    /// zero of the destination row, the second in the middle of it. An algorithm that always appends
    /// passes neither, and one that always prepends passes only the first.
    /// </remarks>
    [Fact]
    public void A_cell_dropped_on_another_row_lands_where_the_pointer_names()
    {
        var editor = Editor(
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "k0" },
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "k1" },
            new SurfaceTextElement { Row = 2, Weight = 1, Key = "k2" },
            new SurfaceTextElement { Row = 2, Weight = 1, Key = "k3" });

        var moved = editor.Elements[0];

        editor.BeginReorder(moved);
        editor.ReorderTo(10, RowCentre(2));
        editor.CommitReorder();

        // Before the first cell of row 2, and after its old row's only survivor.
        Assert.Equal(["k1", "k0", "k2", "k3"], Order(editor));
        Assert.Equal(2, moved.Element.Row);

        // The cell is drawn on the row it joined, not where it used to be: Top is the one property a
        // row change has to move, and a result that only rewrote Row would leave the cell on row 1.
        Assert.Equal(editor.Elements.Single(cell => cell.Key == "k2").Top, moved.Top);
    }

    [Fact]
    public void A_cell_dropped_between_two_cells_of_another_row_lands_between_them()
    {
        var editor = Editor(
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "k0" },
            new SurfaceTextElement { Row = 2, Weight = 1, Key = "k1" },
            new SurfaceTextElement { Row = 2, Weight = 1, Key = "k2" });

        var moved = editor.Elements[0];

        editor.BeginReorder(moved);

        // Row 2 holds two equal cells, so its midpoint boundary is at 195. Twenty pixels past it is
        // inside the second cell: the drop belongs between them.
        editor.ReorderTo(215, RowCentre(2));
        editor.CommitReorder();

        Assert.Equal(["k1", "k0", "k2"], Order(editor));
        Assert.All(editor.Elements, cell => Assert.Equal(2, cell.Element.Row));
    }

    [Fact]
    public void A_cell_dropped_below_the_last_row_starts_a_row_below_it()
    {
        var editor = Editor(
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "k0" },
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "k1" },
            new SurfaceTextElement { Row = 2, Weight = 1, Key = "k2" });

        var moved = editor.Elements[0];

        editor.BeginReorder(moved);

        // Far below the canvas, which is the shape every "drop past the end" has: the pointer is not
        // in any row, so the answer is a new row under the last one — not row 10, which is where the
        // pointer geometrically is.
        editor.ReorderTo(200, 900);
        editor.CommitReorder();

        Assert.Equal(3, moved.Element.Row);
        Assert.Equal(
            SurfaceElementVM.RowOrigin + (2 * SurfaceElementVM.RowHeight) + (SurfaceElementVM.CellGap / 2),
            moved.Top);
    }

    /// <summary>
    /// A drop on the cell's own place is not an edit.
    /// </summary>
    /// <remarks>
    /// It is how every press that turned out to be a click ends, so it has to be indistinguishable from
    /// the click: the same objects in the same order, the same selection, and the check panel from the
    /// last save still saying what that save found. A commit that runs anyway clears the panel — the
    /// editor losing a finding the author has not read yet, over a gesture that changed nothing.
    /// </remarks>
    [Fact]
    public void A_cell_dropped_on_its_own_place_changes_nothing()
    {
        var store = new RecordingStore([Problem("k1")], Document(3, 1));
        var editor = Editor(store);
        var before = editor.Elements.ToList();

        editor.Save();
        Assert.Single(editor.Diagnostics);
        Assert.Equal(1, store.Saves);

        var middle = editor.Elements[1];

        editor.BeginReorder(middle);
        editor.ReorderTo(middle.Left + (middle.Width / 2), RowCentre(1));
        editor.CommitReorder();

        Assert.Equal(["k0", "k1", "k2"], Order(editor));
        Assert.Equal(before, editor.Elements);

        // Nothing was written, because there was nothing new to write.
        Assert.Equal(1, store.Saves);

        // And the panel still describes the last save, unchanged.
        Assert.Single(editor.Diagnostics);
        Assert.Equal("Saved, with problems.", editor.Status);

        Assert.Same(middle, editor.Selected);
        Assert.False(editor.DropIndicator.IsVisible);
        Assert.False(editor.IsReordering);
    }

    /// <summary>
    /// The dragged cell is still the selected one afterwards, and the inspector has followed it.
    /// </summary>
    /// <remarks>
    /// The row is typed into a field generated from the selected element, so a drop on another row
    /// leaves that field saying a number the element no longer has unless the field list is rebuilt.
    /// This is the assertion that sees both halves: the cell survived the drop, and the inspector was
    /// told what changed about it.
    /// </remarks>
    [Fact]
    public void The_cell_that_was_dragged_is_still_the_one_selected()
    {
        var editor = Editor(
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "k0" },
            new SurfaceTextElement { Row = 2, Weight = 1, Key = "k1" });

        var moved = editor.Elements[1];

        editor.Select(moved);
        editor.BeginReorder(moved);
        editor.ReorderTo(300, RowCentre(1));
        editor.CommitReorder();

        Assert.Same(moved, editor.Selected);
        Assert.True(moved.IsSelected);
        Assert.Equal(1, moved.Element.Row);

        var rowField = Assert.Single(editor.Fields, field => field.Label == "row");
        Assert.Equal("1", rowField.Value);
    }

    /// <summary>
    /// While the pointer is moving, the document is untouched: only the drop line follows it.
    /// </summary>
    /// <remarks>
    /// This is the requirement §13.1 states directly — "do not rebuild the collection during the drag"
    /// — and the one whose violation is hardest to see from outside, because the final file ends up
    /// correct in either implementation. What breaks is the gesture: a rebuilt cell has lost the
    /// pointer capture, so the drag stops on the first pixel of movement.
    /// </remarks>
    [Fact]
    public void A_drag_rewrites_no_cell_while_the_pointer_is_moving()
    {
        var editor = Editor(Document(3, 1));

        var before = editor.Elements.ToList();
        var widths = editor.Elements.Select(cell => cell.Width).ToList();
        var rows = editor.Elements.Select(cell => cell.Element.Row).ToList();

        editor.BeginReorder(editor.Elements[1]);
        editor.ReorderTo(20, RowCentre(1));

        Assert.Equal(before, editor.Elements);
        Assert.Equal(widths, editor.Elements.Select(cell => cell.Width));
        Assert.Equal(rows, editor.Elements.Select(cell => cell.Element.Row));

        // The line did move, so the drag is not simply refusing to say anything.
        Assert.True(editor.DropIndicator.IsVisible);
        Assert.Equal(SurfaceElementVM.CellGap / 2, editor.DropIndicator.Left);
        Assert.Equal(
            SurfaceElementVM.RowOrigin + (SurfaceElementVM.CellGap / 2),
            editor.DropIndicator.Top);
        Assert.Equal(SurfaceElementVM.RowHeight - SurfaceElementVM.CellGap, editor.DropIndicator.Height);
    }

    [Fact]
    public void A_press_that_never_moved_reorders_nothing()
    {
        var editor = Editor(Document(3, 1));
        var before = editor.Elements.ToList();

        editor.BeginReorder(editor.Elements[1]);
        editor.CommitReorder();

        Assert.Equal(before, editor.Elements);
        Assert.False(editor.DropIndicator.IsVisible);
        Assert.False(editor.IsReordering);
    }

    [Fact]
    public void A_cancelled_drag_leaves_the_card_alone()
    {
        var editor = Editor(Document(3, 1));
        var before = editor.Elements.ToList();

        editor.BeginReorder(editor.Elements[1]);
        editor.ReorderTo(20, RowCentre(1));
        editor.CancelReorder();

        Assert.Equal(before, editor.Elements);
        Assert.Equal(["k0", "k1", "k2"], Order(editor));
        Assert.False(editor.DropIndicator.IsVisible);
        Assert.False(editor.IsReordering);
    }

    /// <summary>
    /// The window actually draws the line the view model points at.
    /// </summary>
    /// <remarks>
    /// A view-model test cannot see the two failures most likely here: the template binding never
    /// resolves (a misspelt path silently draws nothing), or the line is a child of the <c>Grid</c>
    /// rather than of a <c>Canvas</c>, in which case <c>Canvas.Left</c> has no effect and every drag
    /// points at the top-left corner. Both look identical from the view model's side.
    /// </remarks>
    [AvaloniaFact]
    public void The_window_draws_the_drop_line_at_the_place_the_drag_names()
    {
        var editor = Editor(Document(3, 1));

        var window = new SurfaceEditorWindow(editor, "drag-test");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var line = window.FindControl<Border>("SurfaceDropLine");

        Assert.NotNull(line);
        Assert.False(line!.IsVisible);

        editor.BeginReorder(editor.Elements[1]);
        editor.ReorderTo(20, RowCentre(1));
        Dispatcher.UIThread.RunJobs();

        Assert.True(line.IsVisible);
        Assert.Equal(editor.DropIndicator.Left, Canvas.GetLeft(line));
        Assert.Equal(editor.DropIndicator.Top, Canvas.GetTop(line));
        Assert.True(line.Bounds.Width > 0, "the drop line was measured to nothing");
    }

    private static IReadOnlyList<SurfaceElement> Document(int count, int row) =>
        [.. Enumerable.Range(0, count).Select(i => (SurfaceElement)new SurfaceTextElement
        {
            Row = row,
            Weight = 1,
            Key = $"k{i}",
        })];

    private static Diagnostic Problem(string key) => new(
        DiagnosticSeverity.Warning,
        "surface.element.unknown-key",
        $"Dropped SurfaceTextElement at row 1, weight 1: the ToolKit does not declare a key named '{key}'.",
        new DiagnosticPath(Key: key));

    private static SurfaceEditorViewModel Editor(params SurfaceElement[] elements) =>
        Editor(new RecordingStore([], elements));

    private static SurfaceEditorViewModel Editor(IReadOnlyList<SurfaceElement> elements) =>
        Editor(new RecordingStore([], elements));

    private static SurfaceEditorViewModel Editor(RecordingStore store)
    {
        var editor = new SurfaceEditorViewModel(store, _ => []);
        editor.Load("drag-test");
        return editor;
    }

    /// <summary>A store that hands back one document, remembers each write, and reports what it is told.</summary>
    private sealed class RecordingStore(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<SurfaceElement> elements) : ISurfaceStore
    {
        /// <summary>How many times the card was written. A no-op drop must not add to this.</summary>
        public int Saves { get; private set; }

        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = [.. elements] }, [], []);

        public void Save(string toolkitId, SurfaceDocument document) => Saves++;

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => diagnostics;

        public void Delete(string toolkitId)
        {
        }

        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
