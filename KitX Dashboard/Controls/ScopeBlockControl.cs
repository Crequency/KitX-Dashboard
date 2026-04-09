using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Dashboard.ViewModels;
using NodifyM.Avalonia.Controls;
using NodifyM.Avalonia.Events;

namespace KitX.Dashboard.Controls;

/// <summary>
/// Custom scope block control inheriting from NodeGroup.
/// Renders as a large bordered container behind regular nodes (ZIndex=-1).
///
/// Key behavior: When the scope block is dragged, directly sets child node controls'
/// Location via the BaseNode CLR setter (not through ViewModel binding) so that
/// BaseNode.LocationChangedEvent fires, which triggers Connector.UpdateAnchor(),
/// which ensures connections redraw correctly.
/// </summary>
public class ScopeBlockControl : NodeGroup
{
    private bool _suppressZIndexManagement;
    private bool _isPropagatingDrag;

    static ScopeBlockControl()
    {
        // Register a class handler for Location changes to propagate drag to child nodes.
        // This fires whether Location is set via CLR setter or SetValue (binding).
        LocationProperty.Changed.AddClassHandler<ScopeBlockControl>((ctrl, e) =>
            ctrl.OnControlLocationChanged(e));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureLowZIndex();
    }

    protected override void OnSelectChanged(NodeSelectEventArgs e)
    {
        base.OnSelectChanged(e);

        // NodifyEditor.SelectItem sets parent ContentPresenter.ZIndex = 1 AFTER
        // this event fires. Use Dispatcher to reset it back to -1 afterward.
        if (!_suppressZIndexManagement)
        {
            Dispatcher.UIThread.Post(EnsureLowZIndex);
        }
    }

    /// <summary>
    /// Called when this scope block's Location AvaloniaProperty changes.
    /// Propagates the movement delta directly to child node controls via their
    /// BaseNode.Location CLR setter, which fires LocationChangedEvent and
    /// causes Connectors to update their Anchors and redraw connections.
    /// </summary>
    private void OnControlLocationChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_isPropagatingDrag) return;

        if (DataContext is not BlueprintScopeBlockVM scopeVm
            || scopeVm.SuppressChildMove
            || scopeVm.Editor == null)
            return;

        var newLocation = (Point)e.NewValue!;
        var oldLocation = e.OldValue != null ? (Point)e.OldValue : default;
        var delta = new Point(newLocation.X - oldLocation.X, newLocation.Y - oldLocation.Y);

        if (delta.X == 0 && delta.Y == 0) return;

        var editor = this.FindAncestorOfType<NodifyEditor>();
        if (editor == null) return;

        // Suppress ViewModel-level propagation and bounds recalculation to avoid
        // double-move and chain reactions between scope blocks during drag
        scopeVm.SuppressChildMove = true;
        scopeVm.SuppressBoundsRecalc = true;
        _isPropagatingDrag = true;
        try
        {
            foreach (var nodeId in scopeVm.ContainedNodeIds)
            {
                var nodeVm = scopeVm.Editor.FindNodeById(nodeId);
                if (nodeVm == null) continue;

                var container = editor.ContainerFromItem(nodeVm);
                if (container is ContentPresenter cp && cp.Child is BaseNode nodeControl)
                {
                    // Set Location through CLR setter to fire LocationChangedEvent
                    // which triggers Connector.UpdateAnchor → connection redraw
                    nodeControl.Location = new Point(
                        nodeControl.Location.X + delta.X,
                        nodeControl.Location.Y + delta.Y);
                }
            }
        }
        finally
        {
            _isPropagatingDrag = false;
            scopeVm.SuppressChildMove = false;
            scopeVm.SuppressBoundsRecalc = false;
        }
    }

    /// <summary>
    /// Ensures this scope block's parent ContentPresenter always has ZIndex = -1,
    /// so the scope block renders behind regular nodes regardless of selection state.
    /// </summary>
    private void EnsureLowZIndex()
    {
        if (Parent is ContentPresenter cp)
        {
            cp.ZIndex = -1;
        }
    }
}
