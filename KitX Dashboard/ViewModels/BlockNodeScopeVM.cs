using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// ViewModel for a BlockNode's sub-graph scope (v5.0).
/// Manages collapse/expand state, child node list, preview data,
/// EntryPoint/ExitPoint port mapping, and debug highlight state.
/// <para>
/// Each BlockNode in the canvas gets one BlockNodeScopeVM that governs its
/// visual container (BlockNodeContainer). When collapsed, the BlockNode shows
/// a preview window and EntryPoint/ExitPoint ports; when expanded, child nodes
/// are visible on the canvas.
/// </para>
/// </summary>
public partial class BlockNodeScopeVM : ObservableObject
{
    /// <summary>Block name from the domain BlockNode</summary>
    [ObservableProperty]
    private string _blockName = string.Empty;

    /// <summary>Whether this block is collapsed (showing preview only).
    /// Default: true (§11.1 — blocks render collapsed; internal structure invisible
    /// until the user double-clicks to expand).</summary>
    [ObservableProperty]
    private bool _isCollapsed = true;

    /// <summary>Ordered list of child node IDs inside this block</summary>
    public ObservableCollection<string> ContainedNodeIds { get; } = [];

    /// <summary>Entry point port info (data inputs to the block)</summary>
    public ObservableCollection<BlockPortInfo> EntryPorts { get; } = [];

    /// <summary>Exit point port info (data outputs from the block)</summary>
    public ObservableCollection<BlockPortInfo> ExitPorts { get; } = [];

    /// <summary>Preview node summaries for the collapsed preview window</summary>
    public ObservableCollection<PreviewNodeInfo> PreviewNodes { get; } = [];

    /// <summary>True when any internal node is the current debug execution point</summary>
    [ObservableProperty]
    private bool _isInternalNodeExecuting;

    /// <summary>Number of child nodes (for header display)</summary>
    public int ChildCount => ContainedNodeIds.Count;

    /// <summary>Reference to the editor ViewModel for layout operations</summary>
    public BlueprintEditorViewModel? Editor { get; set; }

    /// <summary>The BlueprintNodeId of the owning BlockNode</summary>
    public string OwnerBlockNodeId { get; set; } = string.Empty;

    // ─── Collapse / Expand ──────────────────────────────────────────────

    /// <summary>
    /// Applies the initial collapsed state.
    /// <para>
    /// Embedded/SubEditor migration: in Embedded mode the NestedNode template
    /// controls inner-visibility via <c>PART_EmbeddedEditor.IsVisible</c>
    /// (bound to <c>!IsCollapsed</c>), so per-VM <see cref="BlueprintNodeVM.IsVisible"/>
    /// toggling is no longer needed and is skipped here. We still call
    /// <see cref="MapPortsForCollapsed"/> for EntryPoint/ExitPoint port mapping.
    /// </para>
    /// <para>
    /// For <c>#MainBlock</c> (whose children stay flat in the outer
    /// <c>Nodes</c> collection) <see cref="MapPortsForCollapsed"/> is also safe:
    /// its child VMs are top-level and remain visible by default, the loop
    /// below only re-maps ports without hiding them in Embedded mode.
    /// </para>
    /// </summary>
    public void ApplyInitialCollapsedState()
    {
        if (IsCollapsed)
            MapPortsForCollapsed();
    }

    /// <summary>Toggles collapse/expand and triggers push layout</summary>
    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
        Editor?.OnBlockCollapseToggled(this);
    }

    partial void OnIsCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(ChildCount));
        if (value)
        {
            MapPortsForCollapsed();
        }
        else
        {
            MapPortsForExpanded();
        }
    }

    // ─── Port Mapping ───────────────────────────────────────────────────

    /// <summary>
    /// When collapsing: scans for EntryPoint/ExitPoint to map their ports onto
    /// the BlockNode's outer connectors.
    /// <para>
    /// Embedded/SubEditor migration: child node <see cref="BlueprintNodeVM.IsVisible"/>
    /// is no longer toggled here — the NestedNode template's
    /// <c>PART_EmbeddedEditor.IsVisible</c> (bound to <c>!IsCollapsed</c>) and
    /// <c>PART_CollapsedBody.IsVisible</c> (bound to <c>IsCollapsed</c>) handle
    /// visibility natively. Toggling per-VM IsVisible would be a redundant no-op
    /// since children live in <c>InnerEditor.Nodes</c>, not the outer canvas.
    /// </para>
    /// </summary>
    private void MapPortsForCollapsed()
    {
        EntryPorts.Clear();
        ExitPorts.Clear();

        if (Editor == null) return;

        foreach (var nodeId in ContainedNodeIds)
        {
            var nodeVm = Editor.FindNodeById(nodeId);
            if (nodeVm == null) continue;

            // EntryPointNode: has no inputs, one Value output → maps to BlockNode OUTPUT port
            if (nodeVm.NodeType == Core.Contract.Workflow.BlueprintNodeType.EntryPoint)
            {
                var portName = nodeVm.Metadata.TryGetValue("PortName", out var pn) ? pn : "Value";
                EntryPorts.Add(new BlockPortInfo
                {
                    PortName = portName,
                    Direction = PortDirection.Output,
                    OriginalNodeId = nodeId
                });
            }

            // ExitPointNode: has one Value input, no outputs → maps to BlockNode INPUT port
            if (nodeVm.NodeType == Core.Contract.Workflow.BlueprintNodeType.ExitPoint)
            {
                var portName = nodeVm.Metadata.TryGetValue("PortName", out var pn) ? pn : "Value";
                ExitPorts.Add(new BlockPortInfo
                {
                    PortName = portName,
                    Direction = PortDirection.Input,
                    OriginalNodeId = nodeId
                });
            }
        }
    }

    /// <summary>
    /// When expanding: clears EntryPoint/ExitPoint port mapping.
    /// <para>
    /// Embedded/SubEditor migration: no per-VM IsVisible toggling — visibility
    /// is controlled by the NestedNode template.
    /// </para>
    /// </summary>
    private void MapPortsForExpanded()
    {
        EntryPorts.Clear();
        ExitPorts.Clear();
    }

    // ─── Preview Data ───────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds preview node summaries from contained child nodes.
    /// Called when child nodes change or when the block is first loaded.
    /// </summary>
    public void RecalculatePreview()
    {
        PreviewNodes.Clear();
        if (Editor == null) return;

        foreach (var nodeId in ContainedNodeIds)
        {
            var nodeVm = Editor.FindNodeById(nodeId);
            if (nodeVm == null) continue;

            var preview = new PreviewNodeInfo
            {
                NodeId = nodeId,
                DisplayTitle = nodeVm.DisplayTitle ?? nodeVm.Name ?? "?",
                NodeType = nodeVm.NodeType,
                Location = nodeVm.Location,
                IsExecuting = nodeVm.IsExecuting
            };

            // Skip EntryPoint/ExitPoint boundary markers in preview
            if (nodeVm.NodeType == Core.Contract.Workflow.BlueprintNodeType.EntryPoint ||
                nodeVm.NodeType == Core.Contract.Workflow.BlueprintNodeType.ExitPoint)
            {
                preview.IsBoundary = true;
            }

            PreviewNodes.Add(preview);
        }
    }

    // ─── Nesting Check ──────────────────────────────────────────────────

    /// <summary>
    /// Checks whether any child node is itself a BlockNode (nesting is forbidden).
    /// Returns the nested BlockNode's ID if found, null otherwise.
    /// </summary>
    public string? FindNestedBlockNode()
    {
        if (Editor == null) return null;

        foreach (var nodeId in ContainedNodeIds)
        {
            var nodeVm = Editor.FindNodeById(nodeId);
            if (nodeVm != null && nodeVm.NodeType == Core.Contract.Workflow.BlueprintNodeType.Block)
            {
                return nodeId;
            }
        }
        return null;
    }

    // ─── Container Bounds ───────────────────────────────────────────────

    /// <summary>Canvas position of the BlockNode container</summary>
    [ObservableProperty]
    private Point _location;

    /// <summary>Size of the BlockNode container (expanded state)</summary>
    [ObservableProperty]
    private Size _expandedSize = new(400, 300);

    /// <summary>Size of the BlockNode container (collapsed state)</summary>
    [ObservableProperty]
    private Size _collapsedSize = new(280, 120);
}

// ─── Supporting Types ───────────────────────────────────────────────────

/// <summary>Direction of a mapped port</summary>
public enum PortDirection { Input, Output }

/// <summary>Describes a port mapped from EntryPointNode/ExitPointNode to BlockNode</summary>
public class BlockPortInfo
{
    public string PortName { get; set; } = "Value";
    public PortDirection Direction { get; set; }
    public string OriginalNodeId { get; set; } = string.Empty;
}

/// <summary>Preview summary for a node inside a collapsed BlockNode</summary>
public class PreviewNodeInfo
{
    public string NodeId { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public Core.Contract.Workflow.BlueprintNodeType NodeType { get; set; }
    public Point Location { get; set; }
    public bool IsExecuting { get; set; }
    public bool IsBoundary { get; set; }
}
