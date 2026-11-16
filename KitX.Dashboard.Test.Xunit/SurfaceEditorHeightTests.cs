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
/// A card taller than one screen says so, and the panel stops saying it when it is not.
/// </summary>
/// <remarks>
/// <para><b>What this file is guarding.</b> Since 2026-11-16 an empty row draws one slot
/// (§10.2.1.2), so a hand-written <c>row: 100</c> asks the card for 99 blank bands — 6400 pixels of
/// card for a screen that is 845. The rule that made empty rows real therefore owes one guardrail:
/// the author has to be told, because nothing on the canvas does the arithmetic for them.</para>
///
/// <para><b>Why the test counts slots and not rows.</b> Twelve rows where one holds a
/// six-slot <c>List</c> is 17 slots — taller than a screen. Thirteen plain rows is 13 slots — shorter
/// than one. A test counting rows would call the shorter document the offender and the taller one
/// fine. The document below is the twelve-row one for exactly that reason.</para>
///
/// <para><b>Why the editor and not the validator.</b> Which number "one screen" is belongs to the
/// renderer, and <see cref="SurfaceValidator"/> lives in an assembly that may not reference it. The
/// assertion here is therefore on the editor's own finding, with its own code — not on the store's
/// list, which could not have produced it.</para>
///
/// <para><b>What it cannot see.</b> Whether the sentence reads well in any language, and whether a
/// real window shows it. This is the view model's answer; the rendering is covered by the window
/// tests of the panel.</para>
/// </remarks>
public class SurfaceEditorHeightTests
{
    /// <summary>Twelve rows, one of which holds a six-slot list: 11 + 6 = 17 slots = 1088px.</summary>
    private static List<SurfaceElement> TwelveRowsWithOneList()
    {
        var elements = new List<SurfaceElement>();

        for (var row = 1; row <= 11; row++)
            elements.Add(new SurfaceTextElement { Row = row, Weight = 1, Key = "k" + row });

        elements.Add(new SurfaceListElement { Row = 12, Weight = 1, Key = "list" });

        return elements;
    }

    [Fact]
    public void A_card_taller_than_a_screen_carries_a_finding_that_says_how_tall()
    {
        var editor = EditorOver(TwelveRowsWithOneList());

        var finding = Assert.Single(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        // The numbers the finding is made of, asserted rather than only their presence: a sentence
        // that printed the wrong height would still be a warning about the right card, and the whole
        // value of this one is that it does the arithmetic the author did not.
        Assert.Equal("1088", finding.Data!["height"]);
        Assert.Equal("845", finding.Data!["screen"]);
        Assert.Equal("17", finding.Data!["slots"]);
        Assert.Equal("12", finding.Data!["row"]);

        Assert.Contains("1088", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_finding_is_raised_for_a_card_that_fits_one_screen()
    {
        // The same twelve rows with no list: 12 slots = 768px, which fits. This is what tells
        // "twelve rows" apart from "too tall", and it is the direction the old row-count test missed.
        var elements = new List<SurfaceElement>();
        for (var row = 1; row <= 12; row++)
            elements.Add(new SurfaceTextElement { Row = row, Weight = 1, Key = "k" + row });

        var editor = EditorOver(elements);

        Assert.DoesNotContain(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);
    }

    [Fact]
    public void Removing_the_list_withdraws_the_finding()
    {
        var editor = EditorOver(TwelveRowsWithOneList());

        Assert.Contains(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        // Changing the list to a Text is the repair an author would actually make, and it is also a
        // rebuild — which is the only path that recomputes the finding.
        editor.Select(editor.Elements.Single(cell => cell.Element is SurfaceListElement));
        editor.ChangeType(SurfaceElementKind.Text);

        Assert.DoesNotContain(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        // And the canvas agrees: 12 slots now, not 17.
        Assert.Equal(
            SurfaceMetrics.CardHeight,
            Math.Max(SurfaceMetrics.CardHeight, 12 * SurfaceElementVM.SlotHeight));
    }

    [Fact]
    public void An_edit_that_leaves_the_card_too_tall_keeps_saying_so()
    {
        var editor = EditorOver(TwelveRowsWithOneList());

        Assert.Contains(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        // Reordering a row rebuilds the canvas without shrinking the document: the finding has to
        // still be there afterwards, and still be one entry. Sweeping it on every edit and only
        // restoring it on rebuild would make "does the panel mention the height" depend on which
        // gesture the author used last.
        editor.MoveToRow(editor.Elements[0], 3);

        var finding = Assert.Single(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        Assert.Equal("1088", finding.Data!["height"]);
    }

    [Fact]
    public void Saving_a_card_the_store_likes_still_reports_that_it_is_too_tall()
    {
        var editor = EditorOver(TwelveRowsWithOneList(), storeSays: []);

        editor.Save();

        // The store had nothing to say. The card is still 1088px. "Saved." over a panel holding a
        // warning is the window contradicting itself, so the status has to follow the panel.
        Assert.Single(editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);
        Assert.Equal("Saved, with problems.", editor.Status);
    }

    [Fact]
    public void Saving_a_card_that_fits_says_only_that_it_saved()
    {
        var elements = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "k1" },
        };

        var editor = EditorOver(elements, storeSays: []);

        editor.Save();

        Assert.Empty(editor.Diagnostics);
        Assert.Equal("Saved.", editor.Status);
    }

    [Fact]
    public void Closing_a_card_the_store_likes_still_asks_about_its_height()
    {
        // The close path is the last moment the author is told anything, and the dialog that asks
        // "close anyway?" renders exactly this collection. A version that decided from the store's
        // list would let a 1088px card be closed with nobody told — for as long as the window stays
        // open, which is the last time it matters.
        var editor = EditorOver(TwelveRowsWithOneList(), storeSays: []);

        var verdict = editor.PrepareToClose();

        Assert.Equal(SurfaceEditorViewModel.SurfaceCloseVerdict.ConfirmFindings, verdict);

        var finding = Assert.Single(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        Assert.Equal("1088", finding.Data!["height"]);

        // And the status says so for the same reason: "Saved." beside a panel holding a warning is
        // the window contradicting itself.
        Assert.Equal("Saved, with problems.", editor.Status);
    }

    [Fact]
    public void Closing_a_card_that_fits_asks_nothing()
    {
        var editor = EditorOver(
            [new SurfaceTextElement { Row = 1, Weight = 1, Key = "k1" }], storeSays: []);

        Assert.Equal(SurfaceEditorViewModel.SurfaceCloseVerdict.Close, editor.PrepareToClose());
        Assert.Empty(editor.Diagnostics);
        Assert.Equal("Saved.", editor.Status);
    }

    [Fact]
    public void Clearing_the_canvas_withdraws_the_height_finding()
    {
        // Clear rebuilds the boundaries before it sweeps, which is the opposite order from Rebuild.
        // If the sweep decided whether the finding survives, this path and every edit path would
        // disagree about a card that has just become empty.
        var editor = EditorOver(TwelveRowsWithOneList());

        Assert.Contains(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        editor.Clear();

        Assert.DoesNotContain(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);
    }

    [Fact]
    public void Reordering_onto_a_new_row_keeps_the_height_finding()
    {
        // CommitReorder is the other order: it recomputes the boundaries first and sweeps after. The
        // document is unchanged in height, so the finding must survive the sweep that follows it —
        // the case that would vanish silently if the finding were merely excused from sweeping.
        var editor = EditorOver(TwelveRowsWithOneList());

        editor.MoveToRow(editor.Elements[^1], 20);

        var finding = Assert.Single(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        // Rows 1..19 are plain and row 20 holds the six-slot list: 19 + 6 = 25 slots, 1600px.
        Assert.Equal("1600", finding.Data!["height"]);
    }

    [Fact]
    public void A_file_that_cannot_be_read_leaves_no_height_finding_over_an_empty_canvas()
    {
        // The refused-file branch is the one exit that empties the canvas without loading anything.
        // It used to clear the boundary list directly instead of going through RebuildBoundaries, so
        // the previous document's "taller than a screen" finding stayed in the panel over an empty
        // canvas, and the canvas kept the height it had because nothing raised the change. Load is
        // public and production opens a fresh view model per window — which is exactly why this was
        // reachable only from a test, and why the test is the only thing that would have said so.
        var editor = new SurfaceEditorViewModel(new RefusesTheSecondLoad(TwelveRowsWithOneList()), _ => []);

        editor.Load("height-test");

        Assert.Contains(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);
        Assert.Equal(17 * SurfaceElementVM.SlotHeight, editor.CanvasHeight);

        // Select something, so the second load has a selection to take away. Without this the two
        // assertions below hold trivially: nothing was ever selected, so `HasSelection` was already
        // false and `Fields` was already empty, and the test passes against a branch that does not
        // clear the inspector at all. Measured: with `Select(null)` commented out the test stayed
        // green before this line existed.
        editor.Select(editor.Elements[0]);
        Assert.True(editor.HasSelection);
        Assert.NotEmpty(editor.Fields);

        editor.Load("height-test");

        Assert.Empty(editor.Elements);
        Assert.DoesNotContain(
            editor.Diagnostics, diagnostic => diagnostic.Code == SurfaceEditorViewModel.TallerThanAScreenCode);

        // Back to one screen, and saying so — an empty card is not a tall one.
        Assert.Equal(SurfaceMetrics.CardHeight, editor.CanvasHeight);

        // The refusal itself still reaches the author; emptying the findings must not empty this.
        Assert.False(string.IsNullOrWhiteSpace(editor.ValidationMessage));

        // And the inspector let go of the cell it was describing. Not reachable in production (a
        // window loads once into a fresh view model), but a panel offering fields for an element that
        // is not on the canvas is worse than an empty panel: every write through those fields returns
        // silently (`index < 0`), so the author edits and nothing happens and nothing says so.
        Assert.False(editor.HasSelection);
        Assert.Empty(editor.Fields);
    }

    private static SurfaceEditorViewModel EditorOver(
        IReadOnlyList<SurfaceElement> elements,
        IReadOnlyList<Diagnostic>? storeSays = null)
    {
        var editor = new SurfaceEditorViewModel(
            new QuietStore(elements, storeSays ?? []),
            _ => [new KeyDeclaration("k1", "string", KeyRole.Out)]);

        editor.Load("height-test");

        return editor;
    }
    /// <summary>A store whose first load succeeds and whose every load after that refuses the file.</summary>
    /// <remarks>
    /// Two loads on one view model is what makes the refused branch observable at all: the finding can
    /// only be stranded there if there was a previous document to strand it.
    /// </remarks>
    private sealed class RefusesTheSecondLoad(IReadOnlyList<SurfaceElement> elements) : ISurfaceStore
    {
        private int _loads;

        public SurfaceLoadResult Load(string toolkitId)
        {
            _loads++;

            return _loads == 1
                ? new SurfaceLoadResult(new SurfaceDocument { Elements = elements }, [], [])
                : new SurfaceLoadResult(
                    SurfaceDocument.Empty,
                    [Diagnostic.Error("surface.file.unreadable", "the file could not be read")],
                    []);
        }

        public void Save(string toolkitId, SurfaceDocument document)
        {
        }

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => [];

        public void Delete(string toolkitId)
        {
        }

        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }

    /// <summary>A store that hands back one document, writes nowhere, and reports what it is given.</summary>
    private sealed class QuietStore(
        IReadOnlyList<SurfaceElement> elements,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<Diagnostic>? errors = null) : ISurfaceStore
    {
        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = elements }, errors ?? [], []);

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
