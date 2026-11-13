using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// The draggable border between two neighbouring cells of the same row.
/// </summary>
/// <remarks>
/// <para><b>A weight is a share of a row, so the thing a person adjusts is a border, not a
/// number.</b> S4 in the Surface design says exactly this — "drag inside a row to reorder, and to
/// change <c>weight</c>" — and the previous build ignored it: it offered spinners, a tick to apply
/// them, and two sentences explaining that the canvas would not move until the tick was pressed.
/// Those sentences were not documentation, they were the receipt for not having built the drag.</para>
///
/// <para><b>Located in the gap, not on the cell.</b> <see cref="HitWidth"/> equals
/// <see cref="SurfaceElementVM.CellGap"/>, so the grab area covers exactly the space between two
/// cells and never steals a press from a cell's own content. A wider target would be easier to hit
/// and would make the narrowest cells impossible to press.</para>
///
/// <para><b>No draft and no confirm.</b> The drag ends where the pointer is released, and that is
/// the committed value: there is no interval in which the border and the document disagree, so
/// there is nothing to explain and nothing to apply.</para>
///
/// <para><b>It notifies instead of being replaced.</b> This object holds the pointer capture while
/// it is being dragged, so the editor must not rebuild the collection under it — a replaced control
/// has lost the capture and the drag stops on the first pixel of movement. <see cref="Refresh"/>
/// is how the position follows the cells as they resize.</para>
/// </remarks>
public sealed class SurfaceBoundaryVM : INotifyPropertyChanged
{
    /// <summary>How wide the visible grip is, in canvas pixels.</summary>
    public const double GripWidth = 3;

    /// <summary>Creates the border between two cells that share a row.</summary>
    /// <param name="before">The cell on the left.</param>
    /// <param name="after">The cell on the right.</param>
    public SurfaceBoundaryVM(SurfaceElementVM before, SurfaceElementVM after)
    {
        Before = before;
        After = after;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The cell on the left of this border.</summary>
    public SurfaceElementVM Before { get; }

    /// <summary>The cell on the right of this border.</summary>
    public SurfaceElementVM After { get; }

    /// <summary>How wide the grab area is, in canvas pixels.</summary>
    /// <remarks>
    /// An instance property rather than a constant, because XAML cannot bind to a static field and
    /// the alternative — a literal 8 in the template — is the one number that has to keep matching
    /// <see cref="SurfaceElementVM.CellGap"/> for the grip to sit in the gap it is named after.
    /// </remarks>
    public double HitWidth => SurfaceElementVM.CellGap;

    /// <summary>Left edge of the grab area, in canvas pixels.</summary>
    public double Left => Centre - (HitWidth / 2);

    /// <summary>Top edge, in canvas pixels.</summary>
    public double Top => Before.Top;

    /// <summary>Height of the grab area, in canvas pixels.</summary>
    public double Height => Before.Height;

    /// <summary>Where the two cells meet: the middle of the gap between them.</summary>
    /// <remarks>
    /// Derived from the two cells rather than stored, so it cannot disagree with them: a border whose
    /// position was its own field would need updating by whoever moved a cell, and the one place that
    /// forgot would draw a grip beside nothing.
    /// </remarks>
    public double Centre => (Before.Right + After.Left) / 2;

    /// <summary>Whether this border still sits between the two cells it was made for.</summary>
    /// <remarks>
    /// Used when a drag has left the pair in a new order: the border belongs to a pair, and a pair
    /// that is no longer adjacent has no border between it.
    /// </remarks>
    public bool StillSeparates() =>
        Before.Element.Row == After.Element.Row && Before.Right <= After.Left + 0.5;

    /// <summary>Re-reads the two cells' positions after they resized.</summary>
    public void Refresh()
    {
        Raise(nameof(Left));
        Raise(nameof(Top));
        Raise(nameof(Height));
        Raise(nameof(Centre));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
