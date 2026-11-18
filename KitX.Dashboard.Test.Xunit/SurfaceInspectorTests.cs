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
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The inspector offers the properties the file can hold, and writes them back into the document.
/// </summary>
/// <remarks>
/// <para><b>What this file is guarding.</b> Before it, the editor could place an element and delete
/// it, and nothing else: <c>label</c>, <c>name</c>, <c>events</c>, <c>emphasis</c>, <c>min</c>,
/// <c>step</c>, <c>options</c> — every property a card's content is made of — existed only in
/// <c>surface.json</c>. A card could be rearranged but not written. The palette could also produce
/// only the two display kinds, because it was a list of keys and a key is a stored value, so the
/// five interactive kinds were unreachable altogether.</para>
///
/// <para><b>The property names are the file's names, not the screen's.</b> <c>events</c> is labelled
/// <c>events</c>, not "Events" or "事件": this window writes a JSON file, and a label that drifts
/// from the key it writes makes the file harder to read, not the window easier.</para>
///
/// <para><b>The field list is generated from the element's kind.</b> The contract already says which
/// properties each kind has; a second copy of that table in XAML would be a second thing to forget.
/// So what is asserted here is per-kind: a <c>Button</c> has a label and events, a <c>Number</c> has
/// bounds and no events at all (it cannot speak), and a <c>Text</c> has a key.</para>
/// </remarks>
public class SurfaceInspectorTests
{
    [Fact]
    public void A_button_offers_exactly_the_properties_a_button_has()
    {
        var editor = Editor(new SurfaceButtonElement { Label = "Send", Events = ["Submit"], Row = 1 });

        editor.Select(editor.Elements[0]);

        Assert.Equal(
            ["type", "row", "label", "events", "emphasis"],
            editor.Fields.Select(field => field.Label).ToArray());

        Assert.Equal("Send", Field(editor, "label").Value);
        Assert.Equal("Submit", Field(editor, "events").Value);
        Assert.Equal("1", Field(editor, "row").Value);
    }

    /// <summary>
    /// A control that cannot raise an event is not offered one.
    /// </summary>
    /// <remarks>
    /// <c>Number</c> has no <c>events</c> on the contract, and that is a decision rather than an
    /// omission: its only gesture is the one that changes its value, so an event would be a
    /// subscription to "the value changed" and one spinner click would be one spawn.
    /// </remarks>
    [Fact]
    public void A_number_is_not_offered_events_because_it_cannot_speak()
    {
        var editor = Editor(new SurfaceNumberElement { Name = "copies", Row = 1, Min = 0, Max = 9 });

        editor.Select(editor.Elements[0]);

        Assert.Equal(
            ["type", "row", "name", "min", "max", "step", "decimals"],
            editor.Fields.Select(field => field.Label).ToArray());

        Assert.Equal("0", Field(editor, "min").Value);
        Assert.Equal("9", Field(editor, "max").Value);
    }

    /// <summary>Two edits in a row both survive, which a writer closed over the first element fails.</summary>
    [Fact]
    public void Editing_one_property_does_not_undo_the_one_edited_before_it()
    {
        var editor = Editor(new SurfaceButtonElement { Label = "Send", Events = ["Submit"], Row = 1 });

        editor.Select(editor.Elements[0]);

        Field(editor, "label").Value = "Say hello";
        Field(editor, "events").Value = "Submit, Cancel";

        var button = Assert.IsType<SurfaceButtonElement>(Assert.Single(editor.Elements).Element);

        Assert.Equal("Say hello", button.Label);
        Assert.Equal(["Submit", "Cancel"], button.Events);
    }

    [Fact]
    public void A_cleared_optional_property_is_absent_from_the_file_not_an_empty_string()
    {
        var editor = Editor(new SurfaceInputElement { Name = "userInput", Row = 1 });

        editor.Select(editor.Elements[0]);
        Field(editor, "name").Value = "   ";

        Assert.Null(Assert.IsType<SurfaceInputElement>(Assert.Single(editor.Elements).Element).Name);
    }

    [Fact]
    public void Events_are_a_comma_separated_line_and_an_empty_line_sends_nothing()
    {
        var editor = Editor(new SurfaceButtonElement { Label = "Send", Events = ["Submit"], Row = 1 });

        editor.Select(editor.Elements[0]);

        Field(editor, "events").Value = "Click, Submit";
        Assert.Equal(
            ["Click", "Submit"],
            Assert.IsType<SurfaceButtonElement>(editor.Elements[0].Element).Events);

        Field(editor, "events").Value = "";
        Assert.Empty(Assert.IsType<SurfaceButtonElement>(editor.Elements[0].Element).Events);
    }

    /// <summary>
    /// A number typed into a numeric property is read the same way on every machine.
    /// </summary>
    /// <remarks>
    /// Parsed with the invariant culture, because the file is JSON and JSON has one decimal point.
    /// On a machine whose culture writes <c>2,5</c>, a culture-sensitive parse would read the same
    /// file differently — and a property that changes meaning with the system locale is the kind of
    /// bug that only appears in somebody else's session.
    /// </remarks>
    [Fact]
    public void A_number_is_read_as_a_number_and_an_empty_one_as_absent()
    {
        var editor = Editor(new SurfaceNumberElement { Name = "copies", Row = 1 });

        editor.Select(editor.Elements[0]);

        Field(editor, "min").Value = "2.5";
        Assert.Equal(2.5, Assert.IsType<SurfaceNumberElement>(editor.Elements[0].Element).Min);

        Field(editor, "min").Value = "";
        Assert.Null(Assert.IsType<SurfaceNumberElement>(editor.Elements[0].Element).Min);
    }

    [Fact]
    public void A_number_that_is_not_one_is_refused_and_said_out_loud()
    {
        var editor = Editor(new SurfaceNumberElement { Name = "copies", Row = 1, Min = 1 });

        editor.Select(editor.Elements[0]);
        Field(editor, "min").Value = "many";

        Assert.Equal(1, Assert.IsType<SurfaceNumberElement>(editor.Elements[0].Element).Min);
        Assert.False(
            string.IsNullOrWhiteSpace(editor.ValidationMessage),
            "a refused keystroke and a field that does not work look identical in silence");
    }

    /// <summary>
    /// Moving a cell to another row keeps the cell, and keeps it selected.
    /// </summary>
    /// <remarks>
    /// This is why <see cref="SurfaceEditorViewModel.MoveToRow"/> reorders the collection instead of
    /// rebuilding it: the row is typed into a field bound to the selected cell, so a rebuild after the
    /// first character would replace the cell, the field and the keyboard focus together — and "12"
    /// could never be typed. What is asserted is object identity, which is exactly what a rebuild
    /// destroys.
    /// </remarks>
    [Fact]
    public void Moving_a_cell_to_another_row_keeps_the_cell_it_just_moved()
    {
        var editor = Editor(
            new SurfaceTextElement { Key = "title", Row = 1 },
            new SurfaceButtonElement { Label = "Send", Events = ["Submit"], Row = 2 });

        var button = editor.Elements[1];

        editor.Select(button);
        Field(editor, "row").Value = "1";

        Assert.Same(button, editor.Selected);
        Assert.Equal(1, button.Element.Row);

        // After the last cell already on that row, because element order is also left-to-right order.
        Assert.Same(button, editor.Elements[1]);
        Assert.Equal(2, editor.Elements.Count);
    }

    [Fact]
    public void A_row_below_one_is_refused_and_the_element_stays_where_it_was()
    {
        var editor = Editor(new SurfaceButtonElement { Label = "Send", Events = ["Submit"], Row = 3 });

        var cell = editor.Elements[0];

        editor.Select(cell);
        Field(editor, "row").Value = "0";

        Assert.Equal(3, cell.Element.Row);
        Assert.Same(cell, editor.Selected);
        Assert.False(string.IsNullOrWhiteSpace(editor.ValidationMessage));
    }

    [Fact]
    public void Selecting_another_cell_replaces_the_properties_and_the_highlight()
    {
        var editor = Editor(
            new SurfaceTextElement { Key = "title", Row = 1 },
            new SurfaceButtonElement { Label = "Send", Events = ["Submit"], Row = 2 });

        var text = editor.Elements[0];
        var button = editor.Elements[1];

        editor.Select(text);
        Assert.True(text.IsSelected);
        Assert.Contains("key", editor.Fields.Select(field => field.Label));

        editor.Select(button);

        Assert.False(text.IsSelected);
        Assert.True(button.IsSelected);
        Assert.Contains("events", editor.Fields.Select(field => field.Label));
        Assert.DoesNotContain("key", editor.Fields.Select(field => field.Label));
    }

    [Fact]
    public void Nothing_selected_means_no_properties()
    {
        var editor = Editor(new SurfaceButtonElement { Label = "Send", Events = ["Submit"], Row = 1 });

        editor.Select(editor.Elements[0]);
        editor.Select(null);

        Assert.False(editor.HasSelection);
        Assert.Empty(editor.Fields);
    }

    [Fact]
    public void Placing_a_key_selects_what_it_placed()
    {
        var editor = Editor();

        editor.Place(new KeyDeclaration("title", "string", KeyRole.Out));

        Assert.Same(editor.Elements[0], editor.Selected);
        Assert.Equal(
            ["type", "row", "key", "role", "format"],
            editor.Fields.Select(field => field.Label).ToArray());
        Assert.Equal("title", Field(editor, "key").Value);
    }

    /// <summary>
    /// Every kind the palette offers can be placed, and what it places survives its own validator.
    /// </summary>
    /// <remarks>
    /// The name is not decoration. <c>SurfaceValidator</c> drops an editing element that neither names
    /// itself nor sends anything (<c>surface.element.inert</c>), so an <c>Input</c> placed without a
    /// name would vanish the next time the card was opened — the editor handing out cells its own
    /// check panel condemns.
    /// </remarks>
    [Theory]
    [InlineData("Button")]
    [InlineData("Input")]
    [InlineData("Number")]
    [InlineData("Select")]
    [InlineData("Switch")]
    public void Every_kind_the_palette_offers_can_be_placed_and_inspected(string kind)
    {
        var editor = Editor();

        editor.AddKind(kind);

        var placed = Assert.Single(editor.Elements).Element;

        Assert.Same(editor.Elements[0], editor.Selected);
        Assert.Equal("1", Field(editor, "row").Value);

        if (placed is SurfaceInputElement or SurfaceNumberElement or SurfaceSelectElement or SurfaceSwitchElement)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(placed.Name),
                $"a placed {placed.GetType().Name} has no name, so it contributes nothing and would be "
                + "dropped as surface.element.inert.");

            Assert.Null(placed.Key);
        }
    }

    [Fact]
    public void An_unknown_kind_places_nothing()
    {
        var editor = Editor();

        editor.AddKind("Slider");

        Assert.Empty(editor.Elements);
        Assert.False(editor.HasSelection);
    }

    /// <summary>
    /// The window actually shows the fields, and the palette actually renders its buttons.
    /// </summary>
    /// <remarks>
    /// A view-model test cannot see the two things most likely to be wrong here: whether the
    /// <c>ItemsControl</c> repeats per field, and whether the palette's command is bound at all. Both
    /// failures look identical from the view model's side — a populated collection nobody renders.
    /// </remarks>
    [AvaloniaFact]
    public void The_window_shows_the_properties_of_the_cell_that_is_selected()
    {
        var editor = Editor(new SurfaceButtonElement { Label = "Send", Events = ["Submit"], Row = 1 });

        var window = new SurfaceEditorWindow(editor, "inspector-test");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        editor.Select(editor.Elements[0]);
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).ToList();

        Assert.Contains("Send", texts);
        Assert.Contains("Submit", texts);

        // The palette's wiring, as far as a headless test can see it: the template renders the kind,
        // the command resolves $parent[Window].DataContext to *this* editor, and the parameter is the
        // kind itself. Asserting that the word "Switch" is on screen would pass with no command
        // attached at all.
        //
        // Executing it is deliberately not done here: a bound ReactiveCommand in this host raises
        // CanExecuteChanged on ReactiveUI's long-running pool scheduler, and the Button handling it
        // touches an Avalonia property off the UI thread -- "Call from invalid thread", thrown on a
        // background thread, which kills the test host instead of failing one test (measured). The
        // product is not affected: it installs the same scheduler in Program.cs:59 and does all of
        // this on the UI thread. What the command *does* is covered by the view-model tests above.
        var switchButton = window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.GetVisualDescendants().OfType<TextBlock>()
                .Any(block => block.Text == "Switch"));

        Assert.Same(editor.AddKindCommand, switchButton.Command);
        Assert.Equal("Switch", switchButton.CommandParameter);
    }

    private static SurfaceFieldVM Field(SurfaceEditorViewModel editor, string label) =>
        Assert.Single(editor.Fields, field => field.Label == label);

    private static SurfaceEditorViewModel Editor(params SurfaceElement[] elements)
    {
        var editor = new SurfaceEditorViewModel(new FixedStore(elements), _ => []);

        // Load, because Save refuses a card that belongs to no ToolKit — and because the canvas is
        // built from what the store returns, not from the constructor.
        editor.Load("inspector-test");
        return editor;
    }

    /// <summary>A store that hands back one fixed document and writes nowhere.</summary>
    private sealed class FixedStore(IReadOnlyList<SurfaceElement> elements) : ISurfaceStore
    {
        public SurfaceLoadResult Load(string toolkitId) => new(
            new SurfaceDocument { Elements = [.. elements] },
            [],
            []);

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
