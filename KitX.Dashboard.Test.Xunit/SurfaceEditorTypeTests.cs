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
using KitX.ToolKit.View.Avalonia;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// An element can be turned into another kind without leaving the card.
/// </summary>
/// <remarks>
/// <para><b>What this file is guarding.</b> Before it, a <c>Text</c> stayed a <c>Text</c> forever: the
/// palette could add an <c>Input</c>, but the only way to change one already on the canvas was to
/// delete it and build the replacement from nothing — losing its row, its weight, and whatever it
/// had been named. §5 lists "change an element's type" as something the orchestrator does, and it was
/// the one row of that table with no gesture behind it.</para>
///
/// <para><b>The interesting half is what does not survive.</b> The kinds do not share a field set, so
/// a conversion drops more than it keeps: a key belongs to the two display kinds, events to the two
/// kinds that can speak, a name to the four that contribute a value. Those are the rules these tests
/// pin one at a time, because a conversion that quietly kept a field the contract says nothing about
/// would write a <c>surface.json</c> whose own validator complains.</para>
/// </remarks>
public class SurfaceEditorTypeTests
{
    /// <summary>
    /// A <c>Text</c> becomes a <c>Button</c>, and the key it displayed goes back to the palette.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The element has to be a button (no key, a label a person can read), and the
    /// key has to reappear where the author can pick it up again: a conversion that dropped it from the
    /// canvas <em>and</em> from the palette would silently make a declared key unreachable.
    /// </remarks>
    [Fact]
    public void A_text_becoming_a_button_drops_its_key_and_returns_it_to_the_palette()
    {
        var editor = EditorWithKeys(
            [new KeyDeclaration("title", "string", KeyRole.Out)],
            new SurfaceTextElement { Key = "title", Row = 1 });

        editor.Select(editor.Elements[0]);

        Assert.DoesNotContain("title", editor.UnplacedKeys.Select(key => key.Name));

        editor.ChangeType("Button");

        var converted = Assert.IsType<SurfaceButtonElement>(Assert.Single(editor.Elements).Element);
        Assert.Equal("Button", converted.Label);
        Assert.Null(converted.Key);
        Assert.Contains("title", editor.UnplacedKeys.Select(key => key.Name));
    }

    /// <summary>An <c>Input</c> becomes a <c>Select</c>, and keeps the name it contributes under.</summary>
    /// <remarks>
    /// <c>name</c> is the field both kinds are read through: an expression, a trigger condition or an
    /// event payload refers to the element by it. Converting the control that collects a value without
    /// carrying the name would break every reference to it, which is why this one is kept.
    /// </remarks>
    [Fact]
    public void An_input_becoming_a_select_keeps_its_name()
    {
        var editor = Editor(new SurfaceInputElement { Name = "chat", Row = 1 });
        editor.Select(editor.Elements[0]);

        editor.ChangeType("Select");

        var converted = Assert.IsType<SurfaceSelectElement>(Assert.Single(editor.Elements).Element);
        Assert.Equal("chat", converted.Name);

        // And the new kind's own fields start empty rather than carrying the old kind's: an input has
        // no options, so there is nothing to convert into them.
        Assert.Empty(converted.Options);
        Assert.Null(converted.OptionsKey);
    }

    /// <summary>A <c>Button</c> becomes an <c>Input</c> and keeps the events it can raise.</summary>
    /// <remarks>
    /// These two kinds are the only ones that can raise an event, so their <c>events</c> is the same
    /// shape and the same meaning on both sides. Every other conversion clears it: a <c>Number</c>
    /// that kept "Submit" would be a control the runtime has no path to speak through.
    /// </remarks>
    [Fact]
    public void A_button_becoming_an_input_keeps_its_events()
    {
        var editor = Editor(new SurfaceButtonElement { Label = "Send", Events = ["submit"], Row = 1 });
        editor.Select(editor.Elements[0]);

        editor.ChangeType("Input");

        var converted = Assert.IsType<SurfaceInputElement>(Assert.Single(editor.Elements).Element);
        Assert.Equal(["submit"], converted.Events);

        // A name arrives with it, because an input with neither a name nor an event is inert and the
        // validator would drop it the next time the card was opened.
        Assert.Equal("input", converted.Name);
    }

    /// <summary>Events do not follow a kind that cannot raise them.</summary>
    [Fact]
    public void A_button_becoming_a_number_does_not_keep_its_events()
    {
        var editor = Editor(new SurfaceButtonElement { Label = "Send", Events = ["submit"], Row = 1 });
        editor.Select(editor.Elements[0]);

        editor.ChangeType("Number");

        var converted = Assert.IsType<SurfaceNumberElement>(Assert.Single(editor.Elements).Element);
        Assert.Equal("number", converted.Name);
    }

    /// <summary>
    /// A <c>Switch</c> becomes a <c>Number</c> that carries the contract's defaults, not invented bounds.
    /// </summary>
    /// <remarks>
    /// <c>min</c>, <c>max</c>, <c>step</c> and <c>decimals</c> are all optional on
    /// <see cref="SurfaceNumberElement"/>. Writing a range here would be the editor stating a limit
    /// nobody asked for — and the card would then refuse values the author's workflow expects. The
    /// conversion's job is to produce a valid element, not a configured one.
    /// </remarks>
    [Fact]
    public void A_switch_becoming_a_number_gets_no_invented_bounds()
    {
        var editor = Editor(new SurfaceSwitchElement { Name = "enabled", Row = 1 });
        editor.Select(editor.Elements[0]);

        editor.ChangeType("Number");

        var converted = Assert.IsType<SurfaceNumberElement>(Assert.Single(editor.Elements).Element);
        Assert.Equal("enabled", converted.Name);
        Assert.Null(converted.Min);
        Assert.Null(converted.Max);
        Assert.Null(converted.Step);
        Assert.Null(converted.Decimals);
    }

    /// <summary>
    /// A <c>Text</c> can become a <c>List</c> even when its key does not hold an array yet.
    /// </summary>
    /// <remarks>
    /// <b>Allowed, and reported rather than refused.</b> A key's declared type is not exclusive: a
    /// workflow can write an array into a key declared as <c>string</c> after this build's check
    /// existed, and the author may be converting the element now precisely because they are about to
    /// change that. Refusing would leave them with a card they cannot build; the validator answers the
    /// other question on the next check, as <c>surface.element.list.key-not-array</c>. The conversion
    /// therefore keeps the key and says nothing.
    /// </remarks>
    [Fact]
    public void A_text_can_become_a_list_before_its_key_holds_an_array()
    {
        var editor = Editor(new SurfaceTextElement { Key = "title", Row = 1 });
        editor.Select(editor.Elements[0]);

        editor.ChangeType("List");

        var converted = Assert.IsType<SurfaceListElement>(Assert.Single(editor.Elements).Element);
        Assert.Equal("title", converted.Key);
        Assert.Null(converted.ItemTitle);
        Assert.Null(converted.ItemSubtitle);
    }

    /// <summary>
    /// A change of type moves nothing: the cell keeps its row and its share of that row.
    /// </summary>
    /// <remarks>
    /// Row and weight say where the element is, which is a fact about the card rather than about the
    /// kind. They are exactly what the delete-and-recreate route used to lose, and losing them here
    /// would make a type change a silent rearrangement.
    /// </remarks>
    [Fact]
    public void A_type_change_keeps_the_row_and_the_weight()
    {
        var editor = Editor(new SurfaceTextElement { Key = "title", Row = 3, Weight = 2 });
        editor.Select(editor.Elements[0]);

        editor.ChangeType("Button");

        var converted = Assert.Single(editor.Elements).Element;
        Assert.Equal(3, converted.Row);
        Assert.Equal(2, converted.Weight);
    }

    /// <summary>The inspector relists itself for the new kind, and keeps the author on the element.</summary>
    /// <remarks>
    /// This is the only receipt for the fields a conversion dropped: the row that said <c>key</c> is
    /// gone and the rows that say <c>name</c> and <c>events</c> are there. It also has to keep the
    /// selection, because the author's next act is almost always a property of what they just changed.
    /// </remarks>
    [Fact]
    public void The_inspector_lists_the_new_kinds_fields_and_keeps_the_selection()
    {
        var editor = Editor(new SurfaceTextElement { Key = "title", Row = 1 });
        editor.Select(editor.Elements[0]);

        editor.ChangeType("Input");

        Assert.Same(editor.Elements[0], editor.Selected);
        Assert.Equal(
            ["type", "row", "name", "events"],
            editor.Fields.Select(field => field.Label).ToArray());
        Assert.Equal("Input", Field(editor, "type").Value);
    }

    /// <summary>A conversion is an edit, and a conversion to the same kind is not.</summary>
    /// <remarks>
    /// The early return is not an optimisation: <c>Text</c> to <c>Text</c> through the reconstruction
    /// would drop the element's <c>format</c> — a field of the kind being "changed" — and mark the card
    /// dirty for a choice that changed nothing.
    /// </remarks>
    [Fact]
    public void Choosing_the_kind_the_element_already_is_changes_nothing()
    {
        var editor = Editor(new SurfaceTextElement { Key = "title", Format = "percent", Row = 1 });
        editor.Select(editor.Elements[0]);

        var before = editor.Elements[0];

        editor.ChangeType("Text");

        Assert.Same(before, editor.Elements[0]);
        Assert.Equal("percent", Assert.IsType<SurfaceTextElement>(editor.Elements[0].Element).Format);
        Assert.False(editor.IsDirty);
    }

    /// <summary>A kind this build does not know is not applied, and is not an error either.</summary>
    [Fact]
    public void An_unknown_kind_leaves_the_element_alone()
    {
        var editor = Editor(new SurfaceTextElement { Key = "title", Row = 1 });
        editor.Select(editor.Elements[0]);

        var before = editor.Elements[0];

        editor.ChangeType("Slider");

        Assert.Same(before, editor.Elements[0]);
        Assert.False(editor.IsDirty);
    }

    /// <summary>A conversion marks the card dirty, and the new element can be edited straight away.</summary>
    /// <remarks>
    /// The second half is the point of the first: the fields the inspector now holds must write into
    /// the element that is actually on the canvas. A rebuild that left the list pointing at the old
    /// object would look identical to this test's first assertion and fail its second.
    /// </remarks>
    [Fact]
    public void A_converted_element_can_be_edited_immediately()
    {
        var editor = Editor(new SurfaceTextElement { Key = "title", Row = 1 });
        editor.Select(editor.Elements[0]);

        editor.ChangeType("Number");
        Assert.True(editor.IsDirty);

        Field(editor, "name").Value = "copies";

        Assert.Equal("copies", Assert.IsType<SurfaceNumberElement>(Assert.Single(editor.Elements).Element).Name);
    }

    /// <summary>
    /// A converted <c>List</c> is as tall as the renderer draws one, so the cells under it move.
    /// </summary>
    /// <remarks>
    /// The height of a cell is its kind's slot count, which is the second reason a conversion rebuilds
    /// the collection rather than refreshing one cell: refreshing would leave a six-slot cell drawn at
    /// one slot and every row below it in the wrong place.
    /// </remarks>
    [Fact]
    public void A_converted_list_takes_its_six_slots()
    {
        var editor = Editor(
            new SurfaceTextElement { Key = "title", Row = 1 },
            new SurfaceTextElement { Key = "today", Row = 2 });

        editor.Select(editor.Elements[0]);
        editor.ChangeType("List");

        Assert.Equal(SurfaceMetrics.SlotHeight * SurfaceMetrics.ListSlotCount, editor.Elements[0].Height);
        Assert.Equal(SurfaceMetrics.SlotHeight * SurfaceMetrics.ListSlotCount, editor.Elements[1].Top);
    }

    /// <summary>
    /// The window shows the type as a choice of seven, with the element's own kind selected.
    /// </summary>
    /// <remarks>
    /// A view-model test can say the field holds seven choices; only a rendered window can say the
    /// inspector picked the <c>ComboBox</c> branch of the template rather than the free-text one. The
    /// two look identical from the view model, and the wrong branch would give the author a text box
    /// that accepts "Slider".
    /// </remarks>
    [AvaloniaFact]
    public void The_window_offers_the_type_as_a_choice_of_seven()
    {
        var editor = Editor(new SurfaceTextElement { Key = "title", Row = 1 });

        var window = new SurfaceEditorWindow(editor, "type-test");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        editor.Select(editor.Elements[0]);
        Dispatcher.UIThread.RunJobs();

        var picker = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(box => box.ItemsSource is IReadOnlyList<string> choices && choices.Count == 7);

        Assert.Equal(SurfaceElementKind.Of(editor.Elements[0].Element), picker.SelectedItem);
    }

    private static SurfaceFieldVM Field(SurfaceEditorViewModel editor, string label) =>
        Assert.Single(editor.Fields, field => field.Label == label);

    private static SurfaceEditorViewModel Editor(params SurfaceElement[] elements) =>
        EditorWithKeys([], elements);

    private static SurfaceEditorViewModel EditorWithKeys(
        IReadOnlyList<KeyDeclaration> keys,
        params SurfaceElement[] elements)
    {
        var editor = new SurfaceEditorViewModel(new FixedStore(elements), _ => keys);

        editor.Load("type-test");
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
