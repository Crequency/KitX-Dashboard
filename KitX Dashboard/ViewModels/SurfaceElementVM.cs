using System;
using System.Collections.Generic;
using System.Linq;
using KitX.ToolKit.Contract.Surface;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// One element on the orchestrator's canvas, positioned for drawing.
/// </summary>
/// <remarks>
/// <para><b>This is a view of a declaration, not of a value.</b> It carries what is needed to draw a
/// cell — where it is, how big, what kind, and which key it stands for. It never carries the key's
/// current value, and it has no way to ask for one: the orchestrator must not be able to lay out
/// around today's data.</para>
///
/// <para><b>Positions are computed here, not stored on the element.</b> The element keeps its
/// <see cref="SurfaceElement.Row"/> and <see cref="SurfaceElement.Weight"/> because that is what the
/// format records; pixels are a rendering concern that would be wrong to persist.</para>
///
/// <para><b>Horizontal placement needs the row, so this view holds the whole document.</b> Since
/// Q101 a width is a share of <em>its row</em>, so one element cannot be placed without knowing the
/// others on that row. Before Q101 a column index was enough, which is why this class used to take
/// one element and nothing else. Passing the siblings in keeps <see cref="Left"/> and
/// <see cref="Width"/> pure functions of the declaration — the alternative, calling back into the
/// editor's mutable collection, would make a cell's position depend on what was on screen when it
/// was created.</para>
/// </remarks>
public sealed class SurfaceElementVM
{
    /// <summary>Pixels between consecutive rows' top edges.</summary>
    public const double RowHeight = 96;

    /// <summary>Pixels before the first row's top edge.</summary>
    public const double RowOrigin = 24;

    /// <summary>Pixels between cells, horizontally and vertically.</summary>
    public const double CellGap = 8;

    /// <summary>The canvas width this view lays out against.</summary>
    /// <remarks>
    /// Mirrors <c>SurfaceEditorViewModel.CanvasWidth</c>. It is duplicated rather than referenced
    /// because the editor's constant is on the caller and this class should not reach back into it:
    /// every number a cell needs is either its own declaration or this file's.
    /// </remarks>
    public const double CanvasWidth = 390;

    private readonly IReadOnlyList<SurfaceElement> _document;

    /// <summary>Creates the view of one element.</summary>
    /// <param name="element">The element to draw.</param>
    /// <param name="document">
    /// Every element of the same document, in declaration order. Only the ones sharing this
    /// element's row affect its position, but the whole list is taken because that is what the
    /// format has: a row is a group of elements, not a structure of its own.
    /// </param>
    public SurfaceElementVM(SurfaceElement element, IReadOnlyList<SurfaceElement> document)
    {
        Element = element ?? throw new ArgumentNullException(nameof(element));
        _document = document ?? throw new ArgumentNullException(nameof(document));

        DraftWeight = Math.Max(1, Element.Weight);
        DraftRow = Math.Max(1, Element.Row);
    }

    /// <summary>The element this draws. Passed back when removing it.</summary>
    public SurfaceElement Element { get; }

    /// <summary>What the weight box currently says. Not what the canvas is drawn from.</summary>
    /// <remarks>
    /// <para><b>A draft, not a value.</b> <see cref="Element"/> is the value that was committed; this
    /// is what somebody has typed since. The two differ exactly between the moment a number is
    /// entered and the moment it is applied, and that interval is the whole point of the property:
    /// without it the box and the canvas would have no way to disagree, and an edit could not be
    /// begun without also being finished.</para>
    ///
    /// <para><b>The geometry properties below deliberately do not read it.</b> <see cref="Width"/>,
    /// <see cref="Left"/> and <see cref="Top"/> compute from <see cref="Element"/> only, because a
    /// width is a share of the row and the row's other members have drafts of their own — reading
    /// them would make this class reach back into its siblings, which is the one thing the remarks
    /// on <see cref="CanvasWidth"/> forbid. So the canvas shows committed values; a typed number
    /// reaches it when it is applied, and not before.</para>
    ///
    /// <para>Named <c>Draft</c> rather than simply <c>Weight</c> on purpose. A property called
    /// <c>Weight</c> sitting next to <c>Element.Weight</c>, with the drawing code reading one and the
    /// text box the other, is the kind of pair that reads as one thing and behaves as two.</para>
    /// </remarks>
    public int DraftWeight { get; set; }

    /// <summary>What the row box currently says. Not what the canvas is drawn from.</summary>
    /// <remarks>See <see cref="DraftWeight"/>; the reasoning is the same and <see cref="Top"/> reads
    /// <see cref="Element"/> for the same reason.</remarks>
    public int DraftRow { get; set; }

    /// <summary>Which key the element stands for, when it has one.</summary>
    public string? Key => Element.Key;

    /// <summary>The elements sharing this one's row, in declaration order.</summary>
    /// <remarks>
    /// This is what makes a width mean something: Q101 says a weight is a share of the row, so the
    /// row has to be known before any share can be turned into pixels.
    /// </remarks>
    private IReadOnlyList<SurfaceElement> RowSiblings =>
        [.. _document.Where(e => e.Row == Element.Row)];

    /// <summary>Left edge in canvas pixels.</summary>
    /// <remarks>
    /// <para>Sums the widths of the siblings declared before this one, plus one gap between each
    /// pair, and starts half a gap in from the left edge so the outermost cells are inset by the
    /// same amount the gaps are.</para>
    ///
    /// <para><b>With four equal weights this is exactly the old four-column geometry</b> — 89.5px
    /// cells at 4, 101.5, 199, 296.5 — which is the check that the ratio model did not silently
    /// move every existing card.</para>
    /// </remarks>
    public double Left
    {
        get
        {
            var siblings = RowSiblings;
            var total = TotalWeight(siblings);
            if (total <= 0)
                return CellGap / 2;

            var offset = CellGap / 2;
            foreach (var sibling in siblings)
            {
                if (ReferenceEquals(sibling, Element))
                    return offset;

                offset += CellWidth(siblings, sibling, total) + CellGap;
            }

            return offset;
        }
    }

    /// <summary>Top edge in canvas pixels.</summary>
    public double Top => RowOrigin + ((Element.Row - 1) * RowHeight) + (CellGap / 2);

    /// <summary>Width in canvas pixels, from this element's share of its row.</summary>
    /// <remarks>
    /// The row's cells and the gaps between them have to fit inside the canvas together, so the
    /// width available to the cells is the canvas minus one gap per cell — half a gap of inset on
    /// each side. Dividing that by the row's weights is what "three parts to one" means.
    /// </remarks>
    public double Width
    {
        get
        {
            var siblings = RowSiblings;
            var total = TotalWeight(siblings);
            return total <= 0 ? CanvasWidth - CellGap : CellWidth(siblings, Element, total);
        }
    }

    private static int TotalWeight(IReadOnlyList<SurfaceElement> row)
    {
        var total = 0;
        foreach (var element in row)
            total += Math.Max(1, element.Weight);

        return total;
    }

    private static double CellWidth(
        IReadOnlyList<SurfaceElement> row, SurfaceElement element, int totalWeight) =>
        (CanvasWidth - (row.Count * CellGap)) * Math.Max(1, element.Weight) / totalWeight;

    /// <summary>Height in canvas pixels.</summary>
    public double Height => RowHeight - CellGap;

    /// <summary>Pixels of padding inside a cell, on each side.</summary>
    /// <remarks>Mirrors the cell template's <c>Padding</c>. Named because the width arithmetic below
    /// has to subtract it twice, and a literal 8 in three places is how the two drift apart.</remarks>
    public const double CellPadding = 8;

    /// <summary>What the inline weight/row strip needs to lay out without overlapping itself.</summary>
    /// <remarks>
    /// <para>Two 72px spinners, two icon buttons at their natural size, and the 4px margin before the
    /// first tick — measured from the template rather than guessed, and asserted against the template
    /// by <c>SurfaceCellGeometryTests</c> so the two cannot drift.</para>
    ///
    /// <para><b>This number is a fact about the controls, not a policy.</b> Changing a spinner's width
    /// has to change this too, which is the point: the threshold below is derived from it, so making
    /// the strip wider automatically makes it appear in fewer cells.</para>
    /// </remarks>
    public const double ControlStripNaturalWidth = 204;

    /// <summary>The narrowest cell that can hold the inline strip and its padding.</summary>
    public const double FullControlsMinimumWidth = ControlStripNaturalWidth + (CellPadding * 2);

    /// <summary>Whether this cell is wide enough to show the weight/row controls inside it.</summary>
    /// <remarks>
    /// <para><b>A weight is a share of its row, so a cell can be arbitrarily narrow.</b> Ten equal
    /// weights make each cell 31px; the strip needs 220. Drawn inside, the spinners and ticks
    /// overlapped each other and spilled past the cell's border, and no assertion noticed because the
    /// width was correct — 31px is the right width for a tenth of a row. The defect was never the
    /// arithmetic; it was putting fixed-width children into a box whose size is decided elsewhere.</para>
    ///
    /// <para><b>Below the threshold the strip leaves the cell.</b> It does not shrink to 15px, because
    /// a 15px spinner is not a smaller control, it is an unusable one. The cell keeps its label and
    /// gains a button that opens the same controls in a flyout — a popup is laid out against the
    /// window, so it can be the natural size no matter how narrow the cell is. That is what makes the
    /// invariant hold for <em>any</em> weight combination: in compact mode nothing inside the cell has
    /// a width of its own.</para>
    /// </remarks>
    public bool ShowsFullControls => Width >= FullControlsMinimumWidth;

    /// <summary>The element kind, for the cell's corner label.</summary>
    public string Kind => Element switch
    {
        SurfaceTextElement => "Text",
        SurfaceButtonElement => "Button",
        SurfaceInputElement => "Input",
        SurfaceNumberElement => "Number",
        SurfaceSelectElement => "Select",
        SurfaceSwitchElement => "Switch",
        SurfaceListElement => "List",
        _ => "Element",
    };

    /// <summary>What the cell shows in its body: the key, or a button's label.</summary>
    /// <remarks>
    /// Deliberately the <em>key name</em> rather than anything derived from a value. A cell reading
    /// "chat" says what will be drawn there; a cell reading the currently stored chat message says
    /// what happens to be there now, and invites laying out around it.
    /// </remarks>
    public string Label => Element switch
    {
        SurfaceButtonElement button when !string.IsNullOrWhiteSpace(button.Label) => button.Label,
        { Key: { Length: > 0 } key } => key,
        _ => "(no key)",
    };

    /// <summary>A longer description for the tooltip: kind, position and key.</summary>
    /// <remarks>
    /// A projection: every part of it comes from <see cref="Element"/>, which is what lets it live
    /// here instead of in a resource file. The two tooltips the input boxes need are <em>not</em>
    /// projections — they are words written for a person, and they are in the language files like
    /// every other word on this window.
    /// </remarks>
    public string Description =>
        $"{Kind} · row {Element.Row}, weight {Element.Weight}" +
        (Key is null ? string.Empty : $" · key '{Key}'");
}
