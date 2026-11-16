using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View;
using KitX.ToolKit.View.Avalonia;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Closing up blank rows is a command the author runs, and it does exactly one thing.
/// </summary>
/// <remarks>
/// <para><b>What this file is guarding.</b> Since 2026-11-16 an empty row is one slot tall
/// (§10.2.1.2), so deleting the only cell on a row leaves a blank band that nothing else removes.
/// The reversal owed a way out — but as a gesture of its own, because compression moves every row
/// below the gap and doing that as a side effect of a delete would renumber rows the author never
/// touched.</para>
///
/// <para><b>What the command may change and what it may not.</b> Rows are renumbered to 1..n; the
/// order of cells within a row, the weights, the keys and the kinds are all untouched. The tests
/// state each of those separately, because a version that rebuilt the document from scratch would
/// satisfy "the rows are dense now" while quietly resetting every weight to one.</para>
///
/// <para><b>And it says nothing when there is nothing to do.</b> Compression on a dense card is a
/// no-op, and a no-op that rebuilds the canvas would drop the selection and any drag in flight. The
/// assertion for that is on the cell objects themselves, not on the row numbers.</para>
/// </remarks>
public class SurfaceEditorCompressRowsTests
{
    [Fact]
    public void Blank_rows_between_cells_are_closed_up()
    {
        var editor = EditorOver([
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "a" },
            new SurfaceTextElement { Row = 3, Weight = 1, Key = "b" },
            new SurfaceTextElement { Row = 7, Weight = 1, Key = "c" },
        ]);

        Assert.Equal([1, 3, 7], Rows(editor));

        editor.CompressEmptyRows();

        // Rank, not difference: 1, 3, 7 becomes 1, 2, 3 rather than 1, 2, 6 or 1, 3, 5. The gaps are
        // closed completely, which is what "close up" means.
        Assert.Equal([1, 2, 3], Rows(editor));
    }

    [Fact]
    public void Cells_keep_their_order_and_their_weights()
    {
        var editor = EditorOver([
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "a" },
            new SurfaceTextElement { Row = 4, Weight = 5, Key = "b" },
            new SurfaceButtonElement { Row = 4, Weight = 2, Label = "go", Events = ["submit"] },
        ]);

        editor.CompressEmptyRows();

        var after = editor.Elements.Select(cell => cell.Element).ToList();

        // The two cells that shared row 4 still share a row, and still in that order: the element
        // order is also the left-to-right order of a row (Q101).
        Assert.Equal([1, 2, 2], after.Select(element => element.Row));
        Assert.Equal(["a", "b", null], after.Select(element => element.Key));
        Assert.Equal([1, 5, 2], after.Select(element => element.Weight));
    }

    [Fact]
    public void A_dense_card_is_left_alone_and_its_cells_are_not_replaced()
    {
        var editor = EditorOver([
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "a" },
            new SurfaceTextElement { Row = 2, Weight = 3, Key = "b" },
        ]);

        var before = editor.Elements.ToList();

        editor.CompressEmptyRows();

        // Same objects, not equal ones: a rebuild replaces every cell, and a rebuild for nothing
        // would drop the selection and any drag holding a pointer capture.
        Assert.Equal(before.Count, editor.Elements.Count);
        for (var i = 0; i < before.Count; i++)
            Assert.Same(before[i], editor.Elements[i]);

        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Blank_rows_at_the_top_are_closed_too()
    {
        // A document whose lowest row is 4 has no row 1: this is the shape a hand-written file makes,
        // and it is the one where an empty row is most likely to look like a mistake.
        var editor = EditorOver([
            new SurfaceTextElement { Row = 4, Weight = 1, Key = "a" },
            new SurfaceTextElement { Row = 6, Weight = 1, Key = "b" },
        ]);

        editor.CompressEmptyRows();

        Assert.Equal([1, 2], Rows(editor));
    }

    [Fact]
    public void The_command_marks_the_card_dirty_and_forgets_what_the_last_save_said()
    {
        var editor = EditorOver([
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "a" },
            new SurfaceTextElement { Row = 5, Weight = 1, Key = "b" },
        ], storeSays: [Problem()]);

        editor.Save();

        Assert.Single(editor.Diagnostics);
        Assert.False(editor.IsDirty);

        editor.CompressEmptyRows();

        // The document on disk still has row 5, so the canvas no longer describes it — and the
        // finding described the pre-compression document.
        Assert.True(editor.IsDirty);
        Assert.Empty(editor.Diagnostics);
    }

    [Fact]
    public void Compressing_makes_the_canvas_shorter()
    {
        // Four cells on rows 1, 6, 11, 16: 16 slots (1024px) before — past one screen, so the floor
        // of 845 is not what is being measured — and 4 slots (256px) after, which falls back to the
        // floor. The document has to be taller than a screen for this test to be about compression at
        // all: at 13 slots the canvas would sit on its 845px floor on both sides of the command.
        var editor = EditorOver(
        [
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "a" },
            new SurfaceTextElement { Row = 6, Weight = 1, Key = "b" },
            new SurfaceTextElement { Row = 11, Weight = 1, Key = "c" },
            new SurfaceTextElement { Row = 16, Weight = 1, Key = "d" },
        ]);

        var before = editor.CanvasHeight;

        editor.CompressEmptyRows();

        Assert.Equal(16 * SurfaceElementVM.SlotHeight, before);
        Assert.Equal(SurfaceMetrics.CardHeight, editor.CanvasHeight);
    }

    private static int[] Rows(SurfaceEditorViewModel editor) =>
        [.. editor.Elements.Select(cell => cell.Element.Row)];

    private static Diagnostic Problem() => new(
        DiagnosticSeverity.Warning,
        "surface.element.unknown-key",
        "Dropped Text at row 1: the ToolKit does not declare a key named 'a'.",
        new DiagnosticPath(Key: "a"));

    private static SurfaceEditorViewModel EditorOver(
        IReadOnlyList<SurfaceElement> elements,
        IReadOnlyList<Diagnostic>? storeSays = null)
    {
        var editor = new SurfaceEditorViewModel(
            new QuietStore(elements, storeSays ?? []),
            _ => [new KeyDeclaration("a", "string", KeyRole.Out)]);

        editor.Load("compress-test");

        return editor;
    }

    private sealed class QuietStore(
        IReadOnlyList<SurfaceElement> elements,
        IReadOnlyList<Diagnostic> diagnostics) : ISurfaceStore
    {
        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = elements }, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
        }

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
