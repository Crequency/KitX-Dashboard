using System;
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
/// <see cref="SurfaceElement.Row"/>/<see cref="SurfaceElement.Column"/> in grid units because that
/// is what the format records; pixels are a rendering concern that would be wrong to persist.</para>
/// </remarks>
public sealed class SurfaceElementVM
{
    /// <summary>Pixels between consecutive rows' top edges.</summary>
    public const double RowHeight = 96;

    /// <summary>Pixels before the first row's top edge.</summary>
    public const double RowOrigin = 24;

    /// <summary>Pixels between cells, horizontally and vertically.</summary>
    public const double CellGap = 8;

    /// <summary>Creates the view of one element.</summary>
    /// <param name="element">The element to draw.</param>
    public SurfaceElementVM(SurfaceElement element)
    {
        Element = element ?? throw new ArgumentNullException(nameof(element));
    }

    /// <summary>The element this draws. Passed back when removing it.</summary>
    public SurfaceElement Element { get; }

    /// <summary>Which key the element stands for, when it has one.</summary>
    public string? Key => Element.Key;

    /// <summary>Left edge in canvas pixels.</summary>
    public double Left => ((Element.Column - 1) * SurfaceEditorViewModel.ColumnWidth) + (CellGap / 2);

    /// <summary>Top edge in canvas pixels.</summary>
    public double Top => RowOrigin + ((Element.Row - 1) * RowHeight) + (CellGap / 2);

    /// <summary>Width in canvas pixels, honouring the element's column span.</summary>
    public double Width => (Element.ColumnSpan * SurfaceEditorViewModel.ColumnWidth) - CellGap;

    /// <summary>Height in canvas pixels.</summary>
    public double Height => RowHeight - CellGap;

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
    public string Description =>
        $"{Kind} · row {Element.Row}, column {Element.Column}" +
        (Element.ColumnSpan > 1 ? $", span {Element.ColumnSpan}" : string.Empty) +
        (Key is null ? string.Empty : $" · key '{Key}'");
}
