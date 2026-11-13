using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
///
/// <para><b>It notifies, because a drag rewrites it in place.</b> Dragging a border between two
/// cells changes their weights on every pointer move, and the control that is being dragged must
/// survive those updates — so the collection cannot be rebuilt underneath the pointer. Instead the
/// editor replaces the declarations and calls <see cref="Refresh"/>, and the cells redraw. This is
/// the one place where the old "rebuild everything" rule had to bend: it was correct while the only
/// edits came from buttons, and wrong the moment an edit has a duration.</para>
/// </remarks>
public sealed class SurfaceElementVM : INotifyPropertyChanged
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

    private SurfaceElement _element;
    private IReadOnlyList<SurfaceElement> _document;

    /// <summary>Creates the view of one element.</summary>
    /// <param name="element">The element to draw.</param>
    /// <param name="document">
    /// Every element of the same document, in declaration order. Only the ones sharing this
    /// element's row affect its position, but the whole list is taken because that is what the
    /// format has: a row is a group of elements, not a structure of its own.
    /// </param>
    public SurfaceElementVM(SurfaceElement element, IReadOnlyList<SurfaceElement> document)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The element this draws. Passed back when removing it.</summary>
    public SurfaceElement Element => _element;

    /// <summary>Which key the element stands for, when it has one.</summary>
    public string? Key => _element.Key;

    /// <summary>The elements sharing this one's row, in declaration order.</summary>
    /// <remarks>
    /// This is what makes a width mean something: Q101 says a weight is a share of the row, so the
    /// row has to be known before any share can be turned into pixels.
    /// </remarks>
    private IReadOnlyList<SurfaceElement> RowSiblings =>
        [.. _document.Where(e => e.Row == _element.Row)];

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
                if (ReferenceEquals(sibling, _element))
                    return offset;

                offset += CellWidth(siblings, sibling, total) + CellGap;
            }

            return offset;
        }
    }

    /// <summary>Right edge in canvas pixels. What a border is measured from.</summary>
    public double Right => Left + Width;

    /// <summary>Top edge in canvas pixels.</summary>
    public double Top => RowOrigin + ((_element.Row - 1) * RowHeight) + (CellGap / 2);

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
            return total <= 0 ? CanvasWidth - CellGap : CellWidth(siblings, _element, total);
        }
    }

    /// <summary>Height in canvas pixels.</summary>
    public double Height => RowHeight - CellGap;

    /// <summary>Pixels of padding inside a cell, on each side.</summary>
    /// <remarks>Mirrors the cell template's <c>Padding</c>. Named because the width arithmetic has to
    /// subtract it twice, and a literal 8 in three places is how the two drift apart.</remarks>
    public const double CellPadding = 8;

    /// <summary>The element kind, for the cell's corner label.</summary>
    public string Kind => _element switch
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
    public string Label => _element switch
    {
        SurfaceButtonElement button when !string.IsNullOrWhiteSpace(button.Label) => button.Label,
        { Key: { Length: > 0 } key } => key,
        _ => "(no key)",
    };

    /// <summary>A longer description for the tooltip: kind, position and key.</summary>
    /// <remarks>
    /// A projection: every part of it comes from <see cref="Element"/>, which is what lets it live
    /// here instead of in a resource file.
    /// </remarks>
    public string Description =>
        $"{Kind} · row {_element.Row}, weight {_element.Weight}" +
        (Key is null ? string.Empty : $" · key '{Key}'");

    /// <summary>Replaces what this cell draws, keeping the object the pointer may be holding.</summary>
    /// <param name="element">This cell's new declaration.</param>
    /// <param name="document">The whole document after the change.</param>
    /// <remarks>
    /// Both arguments are needed and they are not redundant: a drag changes two cells' weights, so
    /// every cell on that row has a new declaration <em>and</em> new siblings, and <see cref="Left"/>
    /// cannot be computed from either one alone.
    /// </remarks>
    public void Refresh(SurfaceElement element, IReadOnlyList<SurfaceElement> document)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(document);

        _element = element;
        _document = document;

        // One notification per property, because each is bound separately: a cell that redraws its
        // width but keeps its old Left slides the whole row instead of resizing one cell.
        Raise(nameof(Element));
        Raise(nameof(Left));
        Raise(nameof(Right));
        Raise(nameof(Top));
        Raise(nameof(Width));
        Raise(nameof(Height));
        Raise(nameof(Label));
        Raise(nameof(Kind));
        Raise(nameof(Description));
        Raise(nameof(Key));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>The width the whole row has to share, after the gaps are taken out.</summary>
    /// <param name="cellsOnRow">How many cells share the row.</param>
    /// <remarks>
    /// Here rather than on the editor because a drag converts pixels to weights with it on every
    /// pointer move, and the conversion has to use the same arithmetic that produced the pixels.
    /// </remarks>
    public static double ShareableWidth(int cellsOnRow) => CanvasWidth - (cellsOnRow * CellGap);

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
}
