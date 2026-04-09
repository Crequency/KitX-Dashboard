using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Represents a scope block in the blueprint editor.
/// Corresponds to a <c>#Block Name</c> in BlockScript.
/// Created automatically when adding a Branch or Loop node.
/// All scope blocks are at the same level (no nesting) —
/// Branch/Loop nodes inside a scope block use cross-scope connections
/// to reach nodes in other scope blocks.
/// </summary>
public partial class BlueprintScopeBlockVM : ObservableObject
{
    /// <summary>
    /// Unique scope identifier (format: ArmName_OwnerNodeId)
    /// </summary>
    [ObservableProperty]
    private string _scopeId = string.Empty;

    /// <summary>
    /// Display name (defaults to ArmName, user can customize, e.g. "SuccessLogic")
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Semantic arm name ("True", "False", "LoopBody", "LoopEnd")
    /// </summary>
    [ObservableProperty]
    private string _armName = string.Empty;

    /// <summary>
    /// BlueprintNodeId of the Branch/Loop node that owns this scope
    /// </summary>
    [ObservableProperty]
    private string _ownerNodeId = string.Empty;

    /// <summary>
    /// Ordered list of node BlueprintNodeIds contained in this scope
    /// </summary>
    public ObservableCollection<string> ContainedNodeIds { get; } = [];

    /// <summary>
    /// Whether this scope block is collapsed (hides contained nodes)
    /// </summary>
    [ObservableProperty]
    private bool _isCollapsed = false;

    /// <summary>
    /// Position of the NodeGroup on canvas
    /// </summary>
    [ObservableProperty]
    private Avalonia.Point _location = new(0, 0);

    /// <summary>
    /// Size of the NodeGroup
    /// </summary>
    [ObservableProperty]
    private Avalonia.Size _groupSize = new(400, 300);

    /// <summary>
    /// Header background color hex
    /// </summary>
    [ObservableProperty]
    private string _headerColor = "#333333";

    // ─── Drag Propagation & Auto-Sizing ───────────────────────────────

    /// <summary>
    /// Reference to the editor ViewModel for looking up child nodes.
    /// Set when the scope block is created or loaded.
    /// </summary>
    public BlueprintEditorViewModel? Editor { get; set; }

    /// <summary>
    /// Previous location, used to calculate drag delta.
    /// </summary>
    private Avalonia.Point _previousLocation;

    /// <summary>
    /// When true, OnLocationChanged does NOT propagate movement to child nodes.
    /// Set by ScopeBlockControl during drag propagation (control-level moves children
    /// directly via BaseNode CLR setter) and during auto-sizing recalculation.
    /// </summary>
    private bool _suppressChildMove;

    public bool SuppressChildMove
    {
        get => _suppressChildMove;
        set => _suppressChildMove = value;
    }

    /// <summary>
    /// When true, RecalculateBounds does NOT execute.
    /// Used during drag propagation to prevent bounds recalculation
    /// triggered by child node location changes (prevents chain reactions
    /// between scope blocks sharing visual updates).
    /// </summary>
    private bool _suppressBoundsRecalc;

    public bool SuppressBoundsRecalc
    {
        get => _suppressBoundsRecalc;
        set => _suppressBoundsRecalc = value;
    }

    /// <summary>
    /// Tracks subscribed child nodes for PropertyChanged (Location changes).
    /// </summary>
    private readonly Dictionary<string, BlueprintNodeVM> _subscribedNodes = [];

    /// <summary>
    /// Called by CommunityToolkit source generator when Location changes.
    /// Drag propagation is handled by ScopeBlockControl (control layer) which
    /// directly sets BaseNode.Location to fire LocationChangedEvent.
    /// This method only updates _previousLocation for tracking.
    /// </summary>
    partial void OnLocationChanged(Avalonia.Point value)
    {
        _previousLocation = value;
    }

    /// <summary>
    /// Recalculates the scope block's Location and GroupSize from the
    /// bounding box of all contained nodes.
    /// Called when a child node's Location changes (not during scope drag).
    /// </summary>
    public void RecalculateBounds()
    {
        if (_suppressBoundsRecalc || Editor == null || ContainedNodeIds.Count == 0)
            return;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var nodeId in ContainedNodeIds)
        {
            var node = Editor.FindNodeById(nodeId);
            if (node != null)
            {
                minX = Math.Min(minX, node.Location.X);
                minY = Math.Min(minY, node.Location.Y);
                maxX = Math.Max(maxX, node.Location.X + 200); // approximate node width
                maxY = Math.Max(maxY, node.Location.Y + 100); // approximate node height
            }
        }

        if (minX == double.MaxValue)
            return;

        const double padding = 30;
        const double headerHeight = 40;

        _suppressChildMove = true;
        try
        {
            Location = new Avalonia.Point(minX - padding, minY - padding - headerHeight);
            GroupSize = new Avalonia.Size(
                maxX - minX + padding * 2,
                maxY - minY + padding * 2 + headerHeight);
        }
        finally
        {
            _suppressChildMove = false;
        }

        // Update previousLocation to match new Location
        _previousLocation = Location;
    }

    /// <summary>
    /// Subscribes to Location changes on all currently contained child nodes.
    /// When a child moves, the scope block recalculates its bounds.
    /// </summary>
    public void SubscribeToChildNodes()
    {
        foreach (var nodeId in ContainedNodeIds)
        {
            SubscribeToNode(nodeId);
        }

        ContainedNodeIds.CollectionChanged += OnContainedNodeIdsChanged;
    }

    /// <summary>
    /// Unsubscribes from all child node Location changes.
    /// </summary>
    public void UnsubscribeFromChildNodes()
    {
        ContainedNodeIds.CollectionChanged -= OnContainedNodeIdsChanged;

        foreach (var kvp in _subscribedNodes)
        {
            kvp.Value.PropertyChanged -= OnChildNodePropertyChanged;
        }
        _subscribedNodes.Clear();
    }

    private void OnContainedNodeIdsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (string nodeId in e.OldItems)
            {
                UnsubscribeFromNode(nodeId);
            }
        }

        if (e.NewItems != null)
        {
            foreach (string nodeId in e.NewItems)
            {
                SubscribeToNode(nodeId);
            }
        }

        // Recalculate bounds after membership changes
        RecalculateBounds();
    }

    private void SubscribeToNode(string nodeId)
    {
        if (_subscribedNodes.ContainsKey(nodeId) || Editor == null)
            return;

        var node = Editor.FindNodeById(nodeId);
        if (node != null)
        {
            _subscribedNodes[nodeId] = node;
            node.PropertyChanged += OnChildNodePropertyChanged;
        }
    }

    private void UnsubscribeFromNode(string nodeId)
    {
        if (_subscribedNodes.TryGetValue(nodeId, out var node))
        {
            node.PropertyChanged -= OnChildNodePropertyChanged;
            _subscribedNodes.Remove(nodeId);
        }
    }

    private void OnChildNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlueprintNodeVM.Location))
        {
            RecalculateBounds();
        }
    }

    // ─── Static Helpers ───────────────────────────────────────────────

    /// <summary>
    /// Returns the header color based on arm name
    /// </summary>
    public static string GetHeaderColor(string armName) => armName switch
    {
        "True" => "#2E7D32",       // Green
        "False" => "#6A1B9A",      // Purple
        "LoopBody" => "#1565C0",   // Blue
        "LoopEnd" => "#BF360C",    // Deep Orange
        _ => "#333333"             // Gray fallback
    };

    /// <summary>
    /// Returns the default display name suffix based on arm name
    /// </summary>
    public static string GetDefaultDisplayName(string armName) => armName switch
    {
        "True" => "True",
        "False" => "False",
        "LoopBody" => "LoopBody",
        "LoopEnd" => "LoopEnd",
        _ => armName
    };
}
