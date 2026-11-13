using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// What the palette puts on the canvas is a document the rest of the system can draw.
/// </summary>
/// <remarks>
/// <para><b>The palette lists declared keys, and a declared key is a stored value.</b> So every
/// element it places is a key-bound display — <c>Text</c> or <c>List</c>. The four editing kinds
/// (<c>Input</c> / <c>Number</c> / <c>Select</c> / <c>Switch</c>) show what the person typed, carry no
/// key, and are not reachable from a list of keys at all.</para>
///
/// <para><b>This file exists because two of the four mappings broke that.</b> A <c>number</c> key was
/// placed as <c>NumberElement { Key = … }</c> and a <c>boolean</c> key as
/// <c>SwitchElement { Key = … }</c> — an editing kind bound to a key, which is the one shape Q88
/// forbids. Measured consequences, both real:</para>
///
/// <code>
/// SurfaceValidator.Validate on that document:
///   number  → kept=0  codes=[surface.element.key-ignored, surface.element.inert]
///   boolean → kept=0  codes=[surface.element.key-ignored, surface.element.inert]
///   string  → kept=1  codes=[]
///   array   → kept=1  codes=[]
/// </code>
///
/// <para>and, if such an element ever reached the renderer with a value behind it, <c>SetValue</c>
/// threw <c>NotSupportedException: no way to show a value in NumericUpDown</c> — the renderer's
/// <c>default:</c> arm, which is correct about its own contract and fatal about this document.</para>
///
/// <para><b>Neither consequence was reachable in the product, which is why this lived.</b>
/// <c>SurfaceValidator</c> has no production call site, so nothing dropped the element; and
/// <c>Number</c>/<c>Switch</c> values are only written by a workflow, so nothing pushed a value at
/// it. Two missing wires, one silently broken author.</para>
/// </remarks>
public sealed class SurfacePaletteTests
{
    /// <summary>Every kind the palette can place carries what its own kind requires.</summary>
    /// <remarks>
    /// A display kind must name a key, because that key is what it shows. An editing kind must not
    /// name one (Q88), and must say what it uploads as, or it contributes nothing and is inert.
    /// </remarks>
    [Theory]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("boolean")]
    [InlineData("array")]
    public void What_the_palette_places_is_drawable_and_obeys_Q88(string type)
    {
        var editor = Editor();

        editor.Place(new KeyDeclaration("k", type, KeyRole.Out));

        var placed = Assert.Single(editor.Elements).Element;

        if (placed is SurfaceInputElement or SurfaceNumberElement or SurfaceSelectElement or SurfaceSwitchElement)
        {
            Assert.True(
                string.IsNullOrEmpty(placed.Key),
                $"a '{type}' key became a {placed.GetType().Name} bound to key '{placed.Key}'. "
                + "Q88: an editing element shows what the person typed, so it carries no key — "
                + "binding one is the shape the validator drops with surface.element.key-ignored.");

            Assert.False(
                string.IsNullOrWhiteSpace(placed.Name),
                $"a '{type}' key became a {placed.GetType().Name} with no name and no events. "
                + "It contributes no value and sends nothing, so it is inert.");
        }
        else
        {
            Assert.False(
                string.IsNullOrWhiteSpace(placed.Key),
                $"a '{type}' key became a {placed.GetType().Name} with no key, so it displays nothing.");
        }
    }

    /// <summary>A switch over the four declared key types lands on a kind that exists for that type.</summary>
    /// <remarks>
    /// The assertion above can be satisfied by placing <c>Text</c> for all four, and that would be
    /// wrong for <c>array</c> — a <c>Text</c> cannot draw a list. This one pins the pair that must
    /// differ, so a fix that flattens every type onto one element is not accepted.
    /// </remarks>
    [Fact]
    public void An_array_key_becomes_a_list_and_a_scalar_key_does_not()
    {
        var editor = Editor();

        editor.Place(new KeyDeclaration("rows", "array", KeyRole.Stream));
        editor.Place(new KeyDeclaration("title", "string", KeyRole.Out));

        Assert.IsType<SurfaceListElement>(editor.Elements[0].Element);
        Assert.IsNotType<SurfaceListElement>(editor.Elements[1].Element);
    }

    private static SurfaceEditorViewModel Editor()
    {
        var editor = new SurfaceEditorViewModel(new NoStore(), _ => []);
        editor.Load("palette-test");
        return editor;
    }

    /// <summary>A store that hands back an empty document and writes nowhere.</summary>
    private sealed class NoStore : ISurfaceStore
    {
        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = [] }, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
        }

        /// <summary>Nothing to report: this store exists to be loaded from.</summary>
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
