using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Where a dragged cell would land, drawn as a single line.
/// </summary>
/// <remarks>
/// <para><b>A line, not a ghost cell.</b> §13.1 of <c>Package/ToolKit-设计-03-Surface.md</c> settles the
/// visual: a drag draws its landing point and nothing else. A placeholder cell would be a second
/// drawing of the document, and it would have to answer questions the drag does not — what width the
/// cell would take, whether its weight would be renormalised — while the pointer is still moving.
/// A line answers the one question a drag asks: <em>between these two, or here</em>.</para>
///
/// <para><b>Its own object rather than four properties on the editor.</b> A drag rewrites this on
/// every pointer move, and the canvas binds to it. Keeping it apart means the document's change
/// notifications and the drag's transient drawing do not share one channel: a cell listening to the
/// editor would otherwise redraw for a pointer move it has nothing to do with.</para>
/// </remarks>
public sealed class SurfaceDropIndicatorVM : INotifyPropertyChanged
{
    /// <summary>How wide the line is, in canvas pixels.</summary>
    /// <remarks>
    /// Wider than the one-pixel outline it lands between, so it reads as a statement rather than as
    /// one more cell border — and narrow enough that it never covers the cell it points at.
    /// </remarks>
    public const double LineWidth = 3;

    private bool _isVisible;
    private double _left;
    private double _top;
    private double _height;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether there is a landing point to draw.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set => Set(ref _isVisible, value, nameof(IsVisible));
    }

    /// <summary>Left edge of the line, in canvas pixels.</summary>
    public double Left
    {
        get => _left;
        private set => Set(ref _left, value, nameof(Left));
    }

    /// <summary>Top edge of the line, in canvas pixels.</summary>
    public double Top
    {
        get => _top;
        private set => Set(ref _top, value, nameof(Top));
    }

    /// <summary>Height of the line, in canvas pixels: one row's worth.</summary>
    public double Height
    {
        get => _height;
        private set => Set(ref _height, value, nameof(Height));
    }

    /// <summary>Width of the line, in canvas pixels.</summary>
    /// <remarks>
    /// A property rather than a literal in the template, because XAML cannot bind to a static field
    /// and a second copy of the number is a second place to forget it.
    /// </remarks>
    public double Width => LineWidth;

    /// <summary>Points the line at one landing point and shows it.</summary>
    /// <param name="left">Left edge, in canvas pixels.</param>
    /// <param name="top">Top edge, in canvas pixels.</param>
    /// <param name="height">How tall the line is, in canvas pixels.</param>
    public void Show(double left, double top, double height)
    {
        Left = left;
        Top = top;
        Height = height;
        IsVisible = true;
    }

    /// <summary>Takes the line off the canvas.</summary>
    public void Hide() => IsVisible = false;

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
