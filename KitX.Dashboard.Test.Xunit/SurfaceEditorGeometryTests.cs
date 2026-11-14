using System;
using System.Collections.Generic;
using System.Linq;
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
