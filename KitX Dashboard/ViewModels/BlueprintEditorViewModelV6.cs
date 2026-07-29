using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Lens.BpGraphLens;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// BlueprintEditorViewModelV6 — read-only BP canvas ViewModel for the v6 editor.
//
// P1 scope: renders a Blueprint (Contract) produced by BpGraphLens.Project into the
// NodifyM canvas. No editing yet (no Connect override, no node creation, no delete).
// The rendering converts every Contract node → BlueprintNodeVMV6, every connection →
// BlueprintConnectionVMV6, and every ScopeRegion → ScopeFrameVM (background frame).
//
// Editing (Connect with StructuralReducer validation, node palette, delete) arrives
// in P3. See WorkflowV6-Dashboard-Frontend-Design.md §五.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Read-only v6 blueprint editor ViewModel. Renders a Contract Blueprint into NodifyM
/// node/connection VMs plus decorative background frames.
/// </summary>
internal partial class BlueprintEditorViewModelV6 : NodifyEditorViewModelBase
{
    /// <summary>Whether the canvas currently has any nodes to display.</summary>
    [ObservableProperty]
    private bool _hasContent;

    /// <summary>Status banner shown when the canvas is empty.</summary>
    public string EmptyBanner =>
        "蓝图视图为空。在 KS 模式中编写工作流代码后切换到 BP 模式即可看到可视化蓝图。";

    /// <summary>
    /// Renders a Contract Blueprint (+ scope regions) into the canvas. Clears all
    /// existing nodes/connections first.
    /// </summary>
    public void LoadBlueprint(Blueprint blueprint, IReadOnlyList<ScopeRegion> scopes)
    {
        Nodes.Clear();
        Connections.Clear();

        // Phase 1: background frames (added first → bottom ZOrder in ItemsControl).
        foreach (var scope in scopes)
        {
            Nodes.Add(new ScopeFrameVM
            {
                Title = scope.ScopeKind,
                OwnerFunctionName = scope.OwnerFunctionName,
                Depth = scope.Depth,
                Location = new Avalonia.Point(scope.X, scope.Y),
                FrameWidth = scope.Width,
                FrameHeight = scope.Height,
            });
        }

        // Phase 2: create node VMs + their connector VMs.
        var connectorIndex = new Dictionary<(string NodeId, string PinId), BlueprintConnectorVMV6>();
        foreach (var node in blueprint.Nodes)
        {
            var nodeVm = ConvertNodeToViewModel(node, connectorIndex);
            Nodes.Add(nodeVm);
        }

        // Phase 3: create connection VMs (now that all connectors exist).
        foreach (var conn in blueprint.Connections)
        {
            var connectionVm = ConvertConnectionToViewModel(conn, connectorIndex);
            if (connectionVm != null)
                Connections.Add(connectionVm);
        }

        HasContent = blueprint.Nodes.Count > 0;
    }

    /// <summary>Clears the canvas.</summary>
    public void Clear()
    {
        Nodes.Clear();
        Connections.Clear();
        HasContent = false;
    }

    // ── Contract → ViewModel conversion ──

    private static BlueprintNodeVMV6 ConvertNodeToViewModel(
        BlueprintNode node,
        Dictionary<(string NodeId, string PinId), BlueprintConnectorVMV6> connectorIndex)
    {
        var functionName = (node as BuiltinFunctionNode)?.FunctionName;
        var headerColor = BlueprintNodeVMV6.GetHeaderColor(node.NodeType, functionName);
        var displayTitle = node.GetDisplayTitle();

        var nodeVm = new BlueprintNodeVMV6
        {
            BlueprintNodeId = node.Id,
            NodeType = node.NodeType,
            FunctionName = functionName,
            DisplayTitle = displayTitle,
            HeaderColorHex = headerColor,
            Comment = node.Comment,
            Title = displayTitle,
            Location = new Avalonia.Point(node.X, node.Y),
        };

        // Input connectors
        foreach (var pin in node.InputPins)
        {
            var connector = new BlueprintConnectorVMV6
            {
                Title = pin.Name,
                PinType = pin.Type,
                OriginalPinId = pin.Id,
                DefaultValue = pin.DefaultValue,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
            };
            nodeVm.Input.Add(connector);
            connectorIndex[(node.Id, pin.Id)] = connector;
        }

        // Output connectors
        foreach (var pin in node.OutputPins)
        {
            var connector = new BlueprintConnectorVMV6
            {
                Title = pin.Name,
                PinType = pin.Type,
                OriginalPinId = pin.Id,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
            };
            nodeVm.Output.Add(connector);
            connectorIndex[(node.Id, pin.Id)] = connector;
        }

        return nodeVm;
    }

    private BlueprintConnectionVMV6? ConvertConnectionToViewModel(
        BlueprintConnection conn,
        Dictionary<(string NodeId, string PinId), BlueprintConnectorVMV6> connectorIndex)
    {
        if (!connectorIndex.TryGetValue((conn.SourceNodeId, conn.SourcePinId), out var source))
            return null;
        if (!connectorIndex.TryGetValue((conn.TargetNodeId, conn.TargetPinId), out var target))
            return null;

        var connection = new BlueprintConnectionVMV6(this, source, target);
        source.IsConnected = true;
        target.IsConnected = true;
        return connection;
    }
}
