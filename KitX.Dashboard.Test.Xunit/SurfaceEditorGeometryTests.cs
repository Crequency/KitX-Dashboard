using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View;
using KitX.ToolKit.View.Avalonia;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The orchestrator's canvas is the card, measured with the renderer's own functions.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> The editor used to lay cells out on its own numbers — 96px rows
/// and a 24px band above the first one — while the renderer drew 64px slots from the card's top edge.
/// The window therefore showed a card that held eight rows where the real card holds thirteen, and a
/// <c>List</c> that occupies six slots on a phone occupied one here. Nothing failed: the canvas looked
/// like a card, and "what you see is what you get" was simply false. These tests pin the two claims
/// that make it true — where a row starts, and how tall a row is — to values computed by
/// <see cref="SurfaceLayout"/>, so a copy of the arithmetic cannot creep back in.</para>
///
/// <para><b>What is asserted, and at what strength.</b> The numbers are checked in pixels (64, 128,
/// 384) rather than only against the functions that produced them: comparing a call site to the same
/// call site would pass no matter what the slot height became. The last test in the file then checks
/// the general case — every cell's <c>Top</c>/<c>Height</c> equals <see cref="SurfaceLayout"/>'s answer
/// for the same document — which is what keeps the two in step for documents nobody wrote a pixel
/// expectation for.</para>
/// </remarks>
public class SurfaceEditorGeometryTests
{
    private static readonly double Slot = SurfaceMetrics.SlotHeight;

    /// <summary>Three rows of one element each: the plain case, with one slot per row.</summary>
    private static IReadOnlyList<SurfaceElement> ThreeRows() =>
    [
        new SurfaceTextElement { Row = 1, Key = "k0" },
        new SurfaceTextElement { Row = 2, Key = "k1" },
        new SurfaceTextElement { Row = 3, Key = "k2" },
    ];

    [Fact]
    public void A_row_starts_where_the_renderer_puts_it()
    {
        var editor = Editor(ThreeRows());

        Assert.Equal(0, editor.Elements[0].Top);
        Assert.Equal(Slot, editor.Elements[1].Top);
        Assert.Equal(2 * Slot, editor.Elements[2].Top);

        Assert.Equal(Slot, editor.Elements[0].Height);
    }

    /// <summary>
    /// A <c>List</c> occupies six slots, and the row under it starts six slots down.
    /// </summary>
    /// <remarks>
    /// This is the case the old geometry could not express: every element was one row tall, so a list
    /// and the row beneath it were 96px apart whatever the list was. The three numbers below are what
    /// the renderer draws, and the editor has to agree with them or the author is arranging a card that
    /// will not be the one shipped.
    /// </remarks>
    [Fact]
    public void A_list_is_six_slots_tall_and_the_row_under_it_starts_below()
    {
        var editor = Editor(
        [
            new SurfaceListElement { Row = 1, Key = "items" },
            new SurfaceTextElement { Row = 2, Key = "k1" },
        ]);

        var list = editor.Elements[0];
        var under = editor.Elements[1];

        Assert.Equal(0, list.Top);
        Assert.Equal(SurfaceMetrics.ListSlotCount * Slot, list.Height);
        Assert.Equal(SurfaceMetrics.ListSlotCount * Slot, under.Top);
        Assert.Equal(Slot, under.Height);
    }

    /// <summary>
    /// A control sharing a row with a <c>List</c> is drawn in the row's band, not in its own slot.
    /// </summary>
    /// <remarks>
    /// The renderer puts a row's cells in one grid row, so a button beside a six-slot list has a
    /// six-slot cell and its control sits at the top of it (Q100's <c>FieldInset</c>). An editor that
    /// measured each element by its own slot count would draw a 64px button and leave a gap the card
    /// does not have.
    /// </remarks>
    [Fact]
    public void A_button_beside_a_list_takes_the_rows_height()
    {
        var editor = Editor(
        [
            new SurfaceListElement { Row = 1, Weight = 1, Key = "items" },
            new SurfaceButtonElement { Row = 1, Weight = 1, Label = "Send" },
        ]);

        var list = editor.Elements[0];
        var button = editor.Elements[1];

        Assert.Equal(0, list.Top);
        Assert.Equal(0, button.Top);
        Assert.Equal(SurfaceMetrics.ListSlotCount * Slot, list.Height);
        Assert.Equal(SurfaceMetrics.ListSlotCount * Slot, button.Height);
    }

    /// <summary>
    /// A document taller than a screen makes the canvas taller with it.
    /// </summary>
    /// <remarks>
    /// <para><b>The defect this covers had two halves and they were the same number.</b>
    /// <c>CanvasHeight</c> was <c>SurfaceMetrics.CardHeight</c> unconditionally, so the canvas stopped
    /// at 845 while the cells were placed by <see cref="SurfaceLayout.PixelTopOf"/> and reached past
    /// it. The editor's ScrollViewer scrolls that canvas, so the author's rows below 845 existed and
    /// could not be brought into view.</para>
    ///
    /// <para><b>Why the existing tests did not catch it: 845 was the right answer for every document
    /// they used.</b> Thirteen ordinary rows are 832px, which is under the floor, so a constant and a
    /// computed height agree on all of them. The fixture here has to be taller than one screen before
    /// the two answers differ at all — that is the whole reason the defect survived a file full of
    /// geometry tests.</para>
    /// </remarks>
    [Fact]
    public void A_document_taller_than_the_card_makes_the_canvas_taller()
    {
        var elements = new List<SurfaceElement>();
        for (var row = 1; row <= 12; row++)
            elements.Add(new SurfaceTextElement { Row = row, Key = $"k{row}" });

        elements.Add(new SurfaceListElement { Row = 6, Key = "sessions" });

        var editor = Editor(elements);

        // 11 ordinary rows plus a six-slot List above the floor.
        Assert.Equal(17 * Slot, editor.CanvasHeight);

        // And the same number the renderer's own layout function gives, so editor and card cannot
        // drift apart without one of them going red.
        Assert.Equal(
            SurfaceLayout.SlotSpanOf(elements, SurfaceMetrics.ListSlotCount) * Slot,
            editor.CanvasHeight);

        // Every cell is inside the canvas — the claim that was false before, and the reason the
        // canvas exists at all.
        Assert.All(editor.Elements, cell =>
            Assert.True(cell.Top + cell.Height <= editor.CanvasHeight,
                $"row {cell.Element.Row} ends at {cell.Top + cell.Height}, past the {editor.CanvasHeight}px canvas"));
    }

    [Fact]
    public void The_canvas_is_never_shorter_than_one_screen()
    {
        // The floor still applies: a one-element document must produce a full screen, or the scrim
        // and the card outline stop covering the thing the author is arranging on.
        var editor = Editor([new SurfaceTextElement { Row = 1, Key = "title" }]);

        Assert.Equal(SurfaceMetrics.CardHeight, editor.CanvasHeight);
    }

    [Fact]
    public void The_canvas_announces_that_it_grew()
    {
        // CanvasHeight has no setter, so nothing raises a change for it unless the class does. The
        // first load looks correct either way, because the window reads the property as it binds —
        // only an edit after that shows the bug, as a canvas that cannot grow once the author has
        // added rows past the floor.
        var editor = Editor([new SurfaceTextElement { Row = 1, Key = "title" }]);

        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Placing rows past the floor grows the canvas through the path an author uses, rather than
        // by loading a second document: Load reads the canvas fresh at bind time and therefore looks
        // correct even when nothing raises a change.
        //
        // Added one on its own row at a time, because a *move* to a far row cannot show this yet: an
        // empty row still collapses to nothing today (see the design note on empty rows), so a single
        // cell at row 20 asks for two slots and the floor answers 845. Fifteen rows do cross it.
        for (var i = 0; i < 15; i++)
            editor.AddKind(SurfaceElementKind.Button);

        Assert.Contains(nameof(SurfaceEditorViewModel.CanvasHeight), raised);

        // Both halves, because each can be wrong alone: the change was announced, and what it
        // announces is the taller number rather than the floor.
        Assert.Equal(16 * Slot, editor.CanvasHeight);
        Assert.True(editor.CanvasHeight > SurfaceMetrics.CardHeight);
    }

    /// <summary>
    /// The card holds thirteen slots, and the thirteenth is still on the card.
    /// </summary>
    /// <remarks>
    /// The count is the reason <see cref="SurfaceMetrics.SlotHeight"/> is 64: thirteen 64px slots are
    /// 832px, which fits the 845px card. The editor used to fit eight — not because it had decided
    /// eight was right, but because 96px slots happen to fit eight.
    /// </remarks>
    [Fact]
    public void The_canvas_holds_thirteen_slots()
    {
        var editor = Editor([.. Enumerable.Range(1, 13)
            .Select(row => (SurfaceElement)new SurfaceTextElement { Row = row, Key = $"k{row}" })]);

        Assert.Equal(SurfaceMetrics.CardHeight, editor.CanvasHeight);
        Assert.Equal(SurfaceMetrics.CardWidth, editor.CanvasWidth);
        Assert.Equal(13, (int)Math.Floor(editor.CanvasHeight / Slot));

        var last = editor.Elements[12];

        Assert.Equal(12 * Slot, last.Top);
        Assert.True(last.Top + last.Height <= editor.CanvasHeight,
            $"the thirteenth row ends at {last.Top + last.Height}, past the {editor.CanvasHeight}px card");
    }

    /// <summary>
    /// Every cell lands on the pixel <see cref="SurfaceLayout"/> names for it.
    /// </summary>
    /// <remarks>
    /// The general form of the three tests above: one document with all three shapes on it, every cell
    /// compared against the renderer's functions called with the renderer's own numbers. If this and
    /// the pixel expectations ever disagree, the pixel ones are wrong — they are a written-down sample
    /// of what this test computes.
    /// </remarks>
    [Fact]
    public void Every_cell_lands_where_the_renderer_would_draw_it()
    {
        var elements = new List<SurfaceElement>
        {
            new SurfaceListElement { Row = 1, Key = "items" },
            new SurfaceButtonElement { Row = 1, Label = "Send" },
            new SurfaceTextElement { Row = 2, Key = "k0" },
            new SurfaceInputElement { Row = 2, Key = "k1" },
            new SurfaceTextElement { Row = 4, Key = "k3" },
        };

        var editor = Editor(elements);

        foreach (var cell in editor.Elements)
        {
            Assert.Equal(
                SurfaceLayout.PixelTopOf(elements, cell.Element, (int)SurfaceMetrics.SlotHeight,
                    SurfaceMetrics.ListSlotCount),
                cell.Top);

            Assert.Equal(
                SurfaceLayout.SlotHeightOf(elements, cell.Element.Row, SurfaceMetrics.ListSlotCount)
                    * SurfaceMetrics.SlotHeight,
                cell.Height);
        }
    }

    /// <summary>
    /// The window draws the canvas at the card's size, not at a literal in the XAML.
    /// </summary>
    /// <remarks>
    /// The view-model test above asserts the number the view model offers; this one asserts the number
    /// the window actually measured, so removing the binding (and leaving the style's old 390 x 845
    /// literals to win) fails here. Both are needed: a bound property nothing binds is how the height
    /// came to be wrong while every view-model test passed.
    /// </remarks>
    [AvaloniaFact]
    public void The_window_draws_the_card_at_the_renderers_size()
    {
        var editor = Editor(ThreeRows());
        var window = new SurfaceEditorWindow(editor, "test");

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var canvas = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("canvas"));

        Assert.Equal(SurfaceMetrics.CardWidth, canvas.Bounds.Width, tolerance: 0.5);
        Assert.Equal(SurfaceMetrics.CardHeight, canvas.Bounds.Height, tolerance: 0.5);
    }

    /// <summary>
    /// Every cell is drawn where the renderer would draw it, measured off the visual tree.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this test exists.</b> Everything above asserts the numbers the view model
    /// computes. None of them can fail if that number never reaches the screen: the cell template puts
    /// its position in a <c>ControlTheme</c> setter (<c>Canvas.Left</c> / <c>Canvas.Top</c>), and a
    /// setter that is silently not applied leaves every assertion in this file green while every cell
    /// sits in the canvas's top-left corner. That is not a hypothetical: it is what the editor was
    /// reported to look like on 2026-11-14, with the view model's arithmetic provably correct.</para>
    ///
    /// <para><b>What makes it a rendering test and not a second view-model test.</b> The expected value
    /// is <see cref="SurfaceLayout.PixelTopOf"/> — the renderer's own function — but the measured value
    /// is the cell's bounds translated into the canvas's coordinate space, which is where Avalonia has
    /// already applied the attached property. A missing or overwritten setter shows up in the second
    /// and not in the first.</para>
    ///
    /// <para><b>Documents with a list, because that is where the two stand furthest apart.</b> A row of
    /// text sits at 0 or 64, which a <c>StackPanel</c> would produce by accident; a cell under a
    /// six-slot list sits at 384, which nothing but the attached property puts there.</para>
    /// </remarks>
    [AvaloniaFact]
    public void Every_cell_is_drawn_where_the_renderer_would_draw_it()
    {
        var elements = new SurfaceElement[]
        {
            new SurfaceListElement { Row = 1, Key = "items" },
            new SurfaceTextElement { Row = 2, Key = "k1" },
            new SurfaceTextElement { Row = 3, Key = "k2" },
        };

        var editor = Editor(elements);
        var window = new SurfaceEditorWindow(editor, "test");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var drawn = DrawnCells(window);

        Assert.Equal(elements.Length, drawn.Count);

        foreach (var (element, position) in drawn)
        {
            var expectedTop = SurfaceLayout.PixelTopOf(
                elements, element, (int)SurfaceMetrics.SlotHeight, SurfaceMetrics.ListSlotCount);

            Assert.Equal(expectedTop, position.Y, tolerance: 0.5);
        }

        // The corner is what a card whose positions never applied looks like: every cell there at
        // once. Asserting the values above already rules it out; asserting the distinctness states the
        // failure in the form it was reported.
        Assert.Equal(3, drawn.Select(cell => cell.Position.Y).Distinct().Count());
    }

    /// <summary>
    /// Moving a cell past the last occupied row reorders it without adding space, and the canvas follows.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the case that was reported as a bug.</b> A cell on row 2 given row 6 stays at the
    /// same height, which reads as "the row was ignored". It is not: a row number is an ordinal, empty
    /// rows take no height (<c>Package/ToolKit-设计-03-Surface.md</c> §10.2.1.2), so row six with rows
    /// one and two occupied is simply the band after row two. The test pins the behaviour so the next
    /// reader meets it as a decision rather than as a defect.</para>
    ///
    /// <para><b>And the canvas is what is being measured.</b> The assertion is on the drawn rectangle,
    /// so it also covers the rebuild that a row change performs: a rebuild that refreshed the view model
    /// but left each container's attached property where it was would keep the card still while every
    /// number said otherwise.</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_row_past_the_last_occupied_one_reorders_the_card_without_adding_space()
    {
        var editor = Editor([
            new SurfaceTextElement { Row = 1, Key = "k0" },
            new SurfaceTextElement { Row = 2, Key = "k1" },
        ]);

        var window = new SurfaceEditorWindow(editor, "test");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        editor.Select(editor.Elements[1]);
        Dispatcher.UIThread.RunJobs();

        var before = DrawnCells(window).Single(cell => cell.Element.Key == "k1").Position.Y;
        Assert.Equal(SurfaceMetrics.SlotHeight, before, tolerance: 0.5);

        Field(editor, "row").Value = "6";
        Dispatcher.UIThread.RunJobs();

        var after = DrawnCells(window).Single(cell => cell.Element.Key == "k1").Position.Y;

        Assert.Equal(before, after, tolerance: 0.5);

        // Now the ordering itself is turned over: the first cell is sent past the second one, so the
        // two must trade places. This is what proves the canvas is following the document rather than
        // having been frozen at whatever it first drew.
        editor.Select(editor.Elements[0]);
        Dispatcher.UIThread.RunJobs();

        Field(editor, "row").Value = "7";
        Dispatcher.UIThread.RunJobs();

        var moved = DrawnCells(window)
            .ToDictionary(cell => cell.Element.Key, cell => cell.Position.Y);

        Assert.Equal(0, moved["k1"], tolerance: 0.5);
        Assert.Equal(SurfaceMetrics.SlotHeight, moved["k0"], tolerance: 0.5);
    }

    /// <summary>Every cell's drawn position, measured in the coordinate space the positions are set in.</summary>
    /// <remarks>
    /// The reference is the <c>Canvas</c> panel the cells are children of, not the card's border: the
    /// border is one pixel thick, so measuring against it adds that pixel to row one and would make
    /// the first row look wrong on a card that is drawn correctly.
    /// </remarks>
    private static List<(SurfaceElement Element, Point Position)> DrawnCells(Window window)
    {
        var panel = window.GetVisualDescendants()
            .OfType<ItemsControl>()
            .First(control => control.ItemsPanelRoot is Canvas
                && control.DataContext is SurfaceEditorViewModel)
            .ItemsPanelRoot!;

        return [.. window.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("cell") && border.DataContext is SurfaceElementVM)
            .Select(border => (
                ((SurfaceElementVM)border.DataContext!).Element,
                border.TranslatePoint(new Point(0, 0), panel) ?? new Point(double.NaN, double.NaN)))];
    }

    private static SurfaceFieldVM Field(SurfaceEditorViewModel editor, string label) =>
        Assert.Single(editor.Fields, field => field.Label == label);

    private static SurfaceEditorViewModel Editor(IReadOnlyList<SurfaceElement> elements)
    {
        var editor = new SurfaceEditorViewModel(new NoStore(elements), _ => []);
        editor.Load("test");
        return editor;
    }

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
}
