using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.Services;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens.BpGraphLens;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// BlueprintEditorViewModelV6 — v6 blueprint editor ViewModel (editable canvas).
//
// P3-α scope: Connect with strong-constraint validation via the inductive
// invariant model — the graph is always structurally legal because every
// connection is validated (frontend pre-check + StructuralReducer) before
// committing. Hover-preview runs ValidateDetailed on a candidate connection so
// illegal drops are signalled (red pin) before the user releases the mouse.
//
// Data flow (Path A — local Contract double-write):
//   • _workingBlueprint is the authoritative Contract copy; edits update both
//     the Contract and the NodifyM VM in tandem.
//   • Validate/AnalyzeScopes/Reverse all consume _workingBlueprint directly.
//
// See WorkflowV6-Dashboard-Frontend-Design.md §五 and the P3 execution plan.
// ─────────────────────────────────────────────────────────────────────────────

internal partial class BlueprintEditorViewModelV6 : NodifyEditorViewModelBase
{
    private readonly BpGraphLens? _bpGraphLens;
    private readonly BuiltinFunctionRegistry? _registry;
    private readonly IPluginServer? _pluginServer;

    /// <summary>The authoritative Contract blueprint being edited. Null until LoadBlueprint.</summary>
    private Blueprint? _workingBlueprint;

    /// <summary>Public accessor for the working blueprint (Reverse / save consume it).</summary>
    public Blueprint? WorkingBlueprint => _workingBlueprint;

    /// <summary>Contract→VM lookup (rendering connections from Contract).</summary>
    private readonly Dictionary<(string NodeId, string PinId), BlueprintConnectorVMV6> _contractToConnector = new();

    /// <summary>VM→Contract reverse lookup (Connect / hover-preview need Contract pin coords).</summary>
    private readonly Dictionary<BlueprintConnectorVMV6, (string NodeId, string PinId)> _connectorToContract = new();

    /// <summary>Whether the canvas currently has any nodes to display.</summary>
    [ObservableProperty]
    private bool _hasContent;

    /// <summary>Whether scope background frames are visible (P5-A3). Hiding keeps the canvas flat.</summary>
    [ObservableProperty]
    private bool _showScopeFrames = true;

    /// <summary>Active constraint violation shown in the error bar (null = no error).</summary>
    [ObservableProperty]
    private ConstraintViolation? _errorInfo;

    /// <summary>Transient error text shown while hovering over an illegal target pin.</summary>
    [ObservableProperty]
    private string? _hoverErrorText;

    /// <summary>True when there is an active constraint violation to display.</summary>
    public bool HasError => ErrorInfo != null;

    /// <summary>True when a hover-preview rejection message is showing.</summary>
    public bool HasHoverError => !string.IsNullOrEmpty(HoverErrorText);

    partial void OnErrorInfoChanged(ConstraintViolation? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnHoverErrorTextChanged(string? value)
        => OnPropertyChanged(nameof(HasHoverError));

    /// <summary>Status banner shown when the canvas is empty.</summary>
    public string EmptyBanner =>
        "蓝图视图为空。在 KS 模式中编写工作流代码后切换到 BP 模式即可看到可视化蓝图。";

    /// <summary>Parameterless constructor (kept for designer/test compatibility).</summary>
    public BlueprintEditorViewModelV6() { }

    /// <summary>Creates an editable BP editor wired to the backend lens for validation.</summary>
    public BlueprintEditorViewModelV6(BpGraphLens bpGraphLens, BuiltinFunctionRegistry? registry,
        IPluginServer? pluginServer = null)
    {
        _bpGraphLens = bpGraphLens;
        _registry = registry;
        _pluginServer = pluginServer;
        PendingConnection.PropertyChanged += OnPendingConnectionPropertyChanged;
        PopulatePalette();
        RefreshPluginTriggers();
    }

    // ── Loading ──

    /// <summary>
    /// Renders a Contract Blueprint (+ scope regions) into the canvas and stores the
    /// blueprint as the working copy for editing. Clears all existing state first.
    /// </summary>
    public void LoadBlueprint(Blueprint blueprint, IReadOnlyList<ScopeRegion> scopes)
    {
        Nodes.Clear();
        Connections.Clear();
        _contractToConnector.Clear();
        _connectorToContract.Clear();
        _workingBlueprint = blueprint;
        ErrorInfo = null;
        HoverErrorText = null;

        // Phase 1: background frames (added first → bottom ZOrder in ItemsControl).
        foreach (var scope in scopes)
            Nodes.Add(CreateScopeFrame(scope));

        // Phase 2: create node VMs + their connector VMs.
        foreach (var node in blueprint.Nodes)
        {
            var nodeVm = ConvertNodeToViewModel(node);
            Nodes.Add(nodeVm);
        }

        // Phase 3: create connection VMs (now that all connectors exist).
        foreach (var conn in blueprint.Connections)
        {
            var connectionVm = ConvertConnectionToViewModel(conn);
            if (connectionVm != null)
                Connections.Add(connectionVm);
        }

        HasContent = blueprint.Nodes.Count > 0;
    }

    /// <summary>Clears the canvas and discards the working blueprint.</summary>
    public void Clear()
    {
        Nodes.Clear();
        Connections.Clear();
        _contractToConnector.Clear();
        _connectorToContract.Clear();
        _workingBlueprint = null;
        ErrorInfo = null;
        HoverErrorText = null;
        HasContent = false;
    }

    // ── Node palette (P3-β) ──

    /// <summary>Flat list of creatable nodes shown in the palette panel.</summary>
    public ObservableCollection<PaletteItemV6> PaletteItems { get; } = new();

    /// <summary>Fills the palette from the builtin registry + hardcoded control-flow set.</summary>
    private void PopulatePalette()
    {
        PaletteItems.Clear();
        PaletteItems.Add(new PaletteItemV6("if (Branch)", "ControlFlow", "Branch"));
        PaletteItems.Add(new PaletteItemV6("forEach (Each)", "ControlFlow", "Each"));
        PaletteItems.Add(new PaletteItemV6("while (While)", "ControlFlow", "While"));
        PaletteItems.Add(new PaletteItemV6("switch (Switch)", "ControlFlow", "Switch"));
        PaletteItems.Add(new PaletteItemV6("break", "ControlFlow", "break"));
        PaletteItems.Add(new PaletteItemV6("continue", "ControlFlow", "continue"));
        if (_registry != null)
            foreach (var fn in _registry.All.OrderBy(f => f.Name))
                PaletteItems.Add(new PaletteItemV6(fn.Name, "Builtin", fn.Name));
    }

    /// <summary>Creates a node from a palette item and drops it onto the canvas.</summary>
    [RelayCommand]
    private void AddPaletteNode(PaletteItemV6? item)
    {
        if (item == null || _workingBlueprint == null) return;
        BlueprintNode node = item.Kind switch
        {
            "ControlFlow" => NodeFactoryV6.CreateControlFlowNode(item.FunctionName!),
            "Builtin" when _registry != null
                => NodeFactoryV6.CreateBuiltinFunctionNode(item.FunctionName!, _registry),
            _ => null!,
        };
        if (node == null) return;
        AddNodeToCanvas(node);
    }

    private int _nodeCounter;

    private void AddNodeToCanvas(BlueprintNode node)
    {
        node.X = 200 + (_nodeCounter % 5) * 230;
        node.Y = 120 + (_nodeCounter / 5) * 140;
        _nodeCounter++;
        _workingBlueprint!.Nodes.Add(node);
        Nodes.Add(ConvertNodeToViewModel(node));
        HasContent = true;
    }

    // ── Plugin trigger palette (P3-δ) ──

    /// <summary>Dynamic palette of plugin triggers from connected plugins' SupportedTriggers.</summary>
    public ObservableCollection<PluginTriggerPaletteItemV6> PluginTriggers { get; } = new();

    /// <summary>True when at least one plugin trigger is available to add.</summary>
    public bool HasPluginTriggers => PluginTriggers.Count > 0;

    /// <summary>
    /// Refreshes <see cref="PluginTriggers"/> from the connected plugin server's
    /// <c>PluginInfo.SupportedTriggers</c>. Called at construction; the list reflects
    /// plugins that are connected when the editor opens.
    /// </summary>
    private void RefreshPluginTriggers()
    {
        PluginTriggers.Clear();
        if (_pluginServer == null) return;
        foreach (var conn in _pluginServer.Connections)
        {
            if (conn.PluginInfo?.SupportedTriggers == null) continue;
            foreach (var trigger in conn.PluginInfo.SupportedTriggers)
                PluginTriggers.Add(new PluginTriggerPaletteItemV6(conn.PluginInfo.Name, trigger));
        }
        OnPropertyChanged(nameof(HasPluginTriggers));
    }

    /// <summary>
    /// Adds a <see cref="PluginTriggerNode"/> from the dynamic palette. When the canvas
    /// already carries an EntryNode (projected from KS) the trigger node replaces it in
    /// place — keeping exactly one entry — otherwise it is dropped as a new node.
    /// </summary>
    [RelayCommand]
    private void AddPluginTriggerNode(PluginTriggerPaletteItemV6? item)
    {
        if (item == null || _workingBlueprint == null) return;
        var node = NodeFactoryV6.CreatePluginTriggerNode(item.PluginName, item.TriggerName);

        var existing = _workingBlueprint.Nodes.FirstOrDefault(n => n is EntryNode);
        if (existing != null)
        {
            var idx = _workingBlueprint.Nodes.IndexOf(existing);
            node.Id = existing.Id;
            node.X = existing.X;
            node.Y = existing.Y;
            node.OutputPins[0].Id = existing.OutputPins[0].Id;
            _workingBlueprint.Nodes[idx] = node;
        }
        else
        {
            AddNodeToCanvas(node);
        }

        // Rebuild the canvas from the Contract so VM connectors match the new root.
        ReloadCanvas();
    }

    /// <summary>Rebuilds all node/connection VMs from the working blueprint (preserves Contract coordinates).</summary>
    private void ReloadCanvas()
    {
        if (_workingBlueprint == null) return;
        IReadOnlyList<ScopeRegion> scopes = [];
        try { scopes = _bpGraphLens?.AnalyzeScopes(_workingBlueprint) ?? []; }
        catch { /* keep empty scope set */ }
        LoadBlueprint(_workingBlueprint, scopes);
    }

    // ── Connect / Disconnect (P3-α editing core) ──

    /// <summary>
    /// Validates and commits a connection. The frontend pre-check rejects common
    /// mistakes (direction/layer/type/uniqueness); then a candidate edge is added to
    /// the working blueprint and StructuralReducer validates the full graph. If the
    /// graph remains legal the connection is committed, otherwise it is rolled back.
    /// Inductive invariant: the graph is always structurally legal after this call.
    /// </summary>
    public override void Connect(ConnectorViewModelBase source, ConnectorViewModelBase target)
    {
        if (source is not BlueprintConnectorVMV6 src || target is not BlueprintConnectorVMV6 tgt)
            return;
        if (_workingBlueprint == null || _bpGraphLens == null)
            return;

        // 1. Frontend pre-check (fast, pin-precise).
        var preViolation = ValidateConnectionV6(src, tgt);
        if (preViolation != null)
        {
            ErrorInfo = preViolation;
            return;
        }

        // 2. Resolve Contract pin coordinates via reverse lookup.
        if (!_connectorToContract.TryGetValue(src, out var srcPin) ||
            !_connectorToContract.TryGetValue(tgt, out var tgtPin))
            return;

        // Normalise direction: output → input.
        var (outCoord, inCoord, outVm, inVm) = src.Flow == ConnectorViewModelBase.ConnectorFlow.Output
            ? (srcPin, tgtPin, src, tgt)
            : (tgtPin, srcPin, tgt, src);

        // 3. Add candidate edge to the working blueprint, then validate the full graph.
        var candidate = new BlueprintConnection
        {
            Id = Guid.NewGuid().ToString(),
            SourceNodeId = outCoord.NodeId,
            SourcePinId = outCoord.PinId,
            TargetNodeId = inCoord.NodeId,
            TargetPinId = inCoord.PinId,
        };
        _workingBlueprint.Connections.Add(candidate);

        var violation = _bpGraphLens.ValidateDetailed(_workingBlueprint);
        if (IsConnectionStructural(violation))
        {
            _workingBlueprint.Connections.Remove(candidate);
            ErrorInfo = violation;
            return;
        }

        // 4. Commit: create the VM connection + refresh IsConnected + scopes.
        var connVm = new BlueprintConnectionVMV6(this, outVm, inVm);
        Connections.Add(connVm);
        outVm.IsConnected = true;
        inVm.IsConnected = true;
        ErrorInfo = null;
        RefreshScopes();
    }

    /// <summary>
    /// Removes all connections touching the connector, syncing both the VM canvas and
    /// the working blueprint Contract.
    /// </summary>
    public override void DisconnectConnector(ConnectorViewModelBase connector)
    {
        if (connector is not BlueprintConnectorVMV6)
        {
            base.DisconnectConnector(connector);
            return;
        }

        var related = Connections
            .OfType<BlueprintConnectionVMV6>()
            .Where(c => c.Source == connector || c.Target == connector)
            .ToList();

        foreach (var cvm in related)
        {
            Connections.Remove(cvm);
            RemoveConnectionFromWorkingBlueprint(cvm);
        }

        RefreshIsConnected();
        RefreshScopes();
        ErrorInfo = null;
    }

    /// <summary>Deletes all selected nodes and their connections (canvas + Contract).</summary>
    [RelayCommand]
    private void DeleteSelectedNodes()
    {
        if (_workingBlueprint == null) return;
        var toRemove = SelectedNodes.OfType<BlueprintNodeVMV6>().ToList();
        if (toRemove.Count == 0) return;

        var nodeIds = toRemove.Select(n => n.BlueprintNodeId).ToHashSet();

        // Remove connections touching deleted nodes (VM + Contract).
        var relatedConns = Connections
            .OfType<BlueprintConnectionVMV6>()
            .Where(c => (c.Source is BlueprintConnectorVMV6 s
                         && _connectorToContract.TryGetValue(s, out var sc) && nodeIds.Contains(sc.NodeId))
                     || (c.Target is BlueprintConnectorVMV6 t
                         && _connectorToContract.TryGetValue(t, out var tc) && nodeIds.Contains(tc.NodeId)))
            .ToList();
        foreach (var cvm in relatedConns)
        {
            Connections.Remove(cvm);
            RemoveConnectionFromWorkingBlueprint(cvm);
        }

        // Remove nodes + clean connector lookups.
        foreach (var nodeVm in toRemove)
        {
            Nodes.Remove(nodeVm);
            var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == nodeVm.BlueprintNodeId);
            if (contractNode != null)
                _workingBlueprint.Nodes.Remove(contractNode);

            var stale = _connectorToContract
                .Where(kvp => kvp.Value.NodeId == nodeVm.BlueprintNodeId).ToList();
            foreach (var kvp in stale)
            {
                _connectorToContract.Remove(kvp.Key);
                _contractToConnector.Remove(kvp.Value);
            }
        }

        SelectedNodes.Clear();
        RefreshIsConnected();
        RefreshScopes();
        ErrorInfo = null;
    }

    // ── Frontend pre-validation ──

    /// <summary>
    /// Fast local checks covering common operator mistakes. Returns a violation
    /// describing the first failure, or null when the connection passes pre-check.
    /// These overlap with StructuralReducer (KS102/KS111) but run instantly and
    /// pinpoint the offending pin without a full-graph traversal.
    /// </summary>
    private ConstraintViolation? ValidateConnectionV6(
        BlueprintConnectorVMV6 src, BlueprintConnectorVMV6 tgt)
    {
        // Resolve node IDs for highlighting.
        _connectorToContract.TryGetValue(src, out var srcCoord);
        _connectorToContract.TryGetValue(tgt, out var tgtCoord);
        var highlight = new[] { srcCoord.NodeId, tgtCoord.NodeId }
            .Where(id => !string.IsNullOrEmpty(id)).ToArray();

        // Definition nodes (const/var block declarations) don't participate in wiring.
        if (IsDefinitionConnector(src) || IsDefinitionConnector(tgt))
            return new ConstraintViolation("PRE", "Definition",
                "定义型节点不可连线（const/var 声明不参与数据/执行图）。", highlight);

        // Direction: one end must be Output, the other Input.
        bool srcIsOutput = src.Flow == ConnectorViewModelBase.ConnectorFlow.Output;
        bool tgtIsOutput = tgt.Flow == ConnectorViewModelBase.ConnectorFlow.Output;
        if (srcIsOutput == tgtIsOutput)
            return new ConstraintViolation("PRE", "Direction",
                "连线方向错误：必须从输出端拖向输入端。", highlight);

        var output = srcIsOutput ? src : tgt;
        var input = srcIsOutput ? tgt : src;

        // Layer match: Exec↔Exec, Data↔Data.
        if (output.IsExecution != input.IsExecution)
            return new ConstraintViolation("PRE", "Layer",
                "图层不匹配：Exec pin 不能与 Data pin 连接。", highlight);

        // Type compatibility (data pins only): Any is wildcard, else exact match.
        if (!output.IsExecution && !IsTypeCompatible(output.PinType, input.PinType))
            return new ConstraintViolation("PRE", "Type",
                $"类型不兼容：不能将 {output.PinTypeText} 连接到 {input.PinTypeText}。", highlight);

        // Self-connect: both pins on the same node.
        if (!string.IsNullOrEmpty(srcCoord.NodeId) && srcCoord.NodeId == tgtCoord.NodeId)
            return new ConstraintViolation("PRE", "SelfConnect",
                "不允许节点自连接。", highlight);

        // Duplicate edge / unique predecessor (E3) / single data input (D2):
        // reject if the input pin already has an incoming edge.
        bool inputHasIncoming = Connections.OfType<BlueprintConnectionVMV6>()
            .Any(c => c.Target == input);
        if (inputHasIncoming)
        {
            return input.IsExecution
                ? new ConstraintViolation("KS102", "E3",
                    "Exec input 已有前驱，不允许多入边。", highlight)
                : new ConstraintViolation("KS111", "D2",
                    "Data input 已有入边，不允许多入边。", highlight);
        }

        // Duplicate edge: identical (output, input) pair already exists.
        bool duplicate = Connections.OfType<BlueprintConnectionVMV6>()
            .Any(c => c.Source == output && c.Target == input);
        if (duplicate)
            return new ConstraintViolation("PRE", "Duplicate",
                "重复连线：该连接已存在。", highlight);

        return null;
    }

    private static bool IsTypeCompatible(PinType source, PinType target)
    {
        if (source == PinType.Any || target == PinType.Any) return true;
        return source == target;
    }

    /// <summary>True when the connector belongs to a definition node (const/var declaration).</summary>
    private bool IsDefinitionConnector(BlueprintConnectorVMV6 connector)
    {
        if (!_connectorToContract.TryGetValue(connector, out var coord)) return false;
        return FindNodeById(coord.NodeId)?.IsDefinition == true;
    }

    // Violation codes introduced by a connection (rejected during edit). Global-completeness
    // codes (KS100/KS120/KS130) come from orphan nodes / missing declarations and are tolerated
    // during editing — they surface only at the switch/save completeness check.
    private static readonly HashSet<string> ConnectionStructuralCodes = new()
    {
        "KS101", "KS102", "KS111", "KS105", "KS110", "KS140",
    };

    private static bool IsConnectionStructural(ConstraintViolation? v)
        => v != null && ConnectionStructuralCodes.Contains(v.Code);

    // ── Hover preview (inductive invariant enforcement) ──

    private void OnPendingConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PendingConnectionViewModelBase.PreviewTarget))
            UpdateHoverPreview();
    }

    /// <summary>
    /// Runs whenever the dragged connection hovers over a new potential target. Resets
    /// visual state, then simulates the candidate connection against the working
    /// blueprint: if StructuralReducer rejects it the target pin turns red and a reason
    /// is shown. Because the current graph is legal (inductive invariant), any returned
    /// connection-structural violation must have been introduced by this candidate edge.
    /// </summary>
    private void UpdateHoverPreview()
    {
        // Reset all connectors to the default (connectable) state.
        foreach (var nodeVm in Nodes.OfType<BlueprintNodeVMV6>())
        {
            foreach (var c in nodeVm.Input.OfType<BlueprintConnectorVMV6>()) c.CanConnect = true;
            foreach (var c in nodeVm.Output.OfType<BlueprintConnectorVMV6>()) c.CanConnect = true;
        }
        HoverErrorText = null;

        if (PendingConnection.Source is not BlueprintConnectorVMV6 src) return;
        if (PendingConnection.PreviewTarget is not BlueprintConnectorVMV6 tgt) return;

        // Frontend pre-check first (instant, pin-precise feedback).
        var preViolation = ValidateConnectionV6(src, tgt);
        if (preViolation != null)
        {
            tgt.CanConnect = false;
            HoverErrorText = preViolation.Message;
            return;
        }

        // StructuralReducer simulation: tentatively add the edge, validate, then revert.
        if (_workingBlueprint == null || _bpGraphLens == null) return;
        if (!_connectorToContract.TryGetValue(src, out var srcCoord) ||
            !_connectorToContract.TryGetValue(tgt, out var tgtCoord))
            return;

        var (outCoord, inCoord) = src.Flow == ConnectorViewModelBase.ConnectorFlow.Output
            ? (srcCoord, tgtCoord)
            : (tgtCoord, srcCoord);

        var candidate = new BlueprintConnection
        {
            Id = Guid.NewGuid().ToString(),
            SourceNodeId = outCoord.NodeId,
            SourcePinId = outCoord.PinId,
            TargetNodeId = inCoord.NodeId,
            TargetPinId = inCoord.PinId,
        };
        _workingBlueprint.Connections.Add(candidate);
        var violation = _bpGraphLens.ValidateDetailed(_workingBlueprint);
        _workingBlueprint.Connections.Remove(candidate);

        if (IsConnectionStructural(violation))
        {
            tgt.CanConnect = false;
            HoverErrorText = violation!.Message;
        }
    }

    // ── Scope frame refresh ──

    /// <summary>Recomputes scope regions from the working blueprint and refreshes frames.</summary>
    private void RefreshScopes()
    {
        if (_bpGraphLens == null || _workingBlueprint == null) return;
        IReadOnlyList<ScopeRegion> scopes;
        try
        {
            scopes = _bpGraphLens.AnalyzeScopes(_workingBlueprint);
        }
        catch
        {
            return;
        }

        // Remove old scope frames.
        for (int i = Nodes.Count - 1; i >= 0; i--)
            if (Nodes[i] is ScopeFrameVM)
                Nodes.RemoveAt(i);

        // Insert new frames at the front so they sit beneath the nodes (reverse order
        // keeps the original index ordering for nested scopes).
        for (int i = scopes.Count - 1; i >= 0; i--)
            Nodes.Insert(0, CreateScopeFrame(scopes[i]));
    }

    private static ScopeFrameVM CreateScopeFrame(ScopeRegion scope) => new()
    {
        Title = scope.ScopeKind,
        OwnerFunctionName = scope.OwnerFunctionName,
        Depth = scope.Depth,
        Location = new Avalonia.Point(scope.X, scope.Y),
        FrameWidth = scope.Width,
        FrameHeight = scope.Height,
    };

    // ── Helpers ──

    // ── Debug helpers (P4-β) ──

    /// <summary>Finds a node VM by its BlueprintNodeId (= debug statementId n_XXXXXXXX).</summary>
    public BlueprintNodeVMV6? FindNodeById(string nodeId)
        => Nodes.OfType<BlueprintNodeVMV6>().FirstOrDefault(n => n.BlueprintNodeId == nodeId);

    /// <summary>
    /// Finds the primary output connector for a node. Used for wire-value tooltip
    /// mapping: wireId <c>w:{nodeId}</c> → node's first output connector.
    /// </summary>
    public BlueprintConnectorVMV6? FindOutputConnector(string nodeId)
        => FindNodeById(nodeId)?.Output.OfType<BlueprintConnectorVMV6>().FirstOrDefault();

    /// <summary>
    /// Finds a specific input connector by node ID + pin name. Used for control-flow
    /// data-input wire tooltips: wireId <c>w:{ctrlNodeId}:{pinName}</c>.
    /// </summary>
    public BlueprintConnectorVMV6? FindInputConnector(string nodeId, string pinName)
        => FindNodeById(nodeId)?.Input.OfType<BlueprintConnectorVMV6>()
               .FirstOrDefault(c => c.Title == pinName);

    /// <summary>Clears all debug highlight state (IsExecuting / ExecutionCompleted) from every node.</summary>
    public void ClearDebugHighlights()
    {
        foreach (var n in Nodes.OfType<BlueprintNodeVMV6>())
        {
            n.IsExecuting = false;
            n.ExecutionCompleted = false;
        }
    }

    /// <summary>Clears all RuntimeValue tooltips from every connector.</summary>
    public void ClearRuntimeValues()
    {
        foreach (var n in Nodes.OfType<BlueprintNodeVMV6>())
        {
            foreach (var c in n.Input.OfType<BlueprintConnectorVMV6>()) c.RuntimeValue = null;
            foreach (var c in n.Output.OfType<BlueprintConnectorVMV6>()) c.RuntimeValue = null;
        }
    }

    /// <summary>Recomputes IsConnected for every connector from the current Connections set.</summary>
    private void RefreshIsConnected()
    {
        var connected = new HashSet<BlueprintConnectorVMV6>();
        foreach (var c in Connections.OfType<BlueprintConnectionVMV6>())
        {
            if (c.Source is BlueprintConnectorVMV6 s) connected.Add(s);
            if (c.Target is BlueprintConnectorVMV6 t) connected.Add(t);
        }
        foreach (var nodeVm in Nodes.OfType<BlueprintNodeVMV6>())
        {
            foreach (var c in nodeVm.Input.OfType<BlueprintConnectorVMV6>())
                c.IsConnected = connected.Contains(c);
            foreach (var c in nodeVm.Output.OfType<BlueprintConnectorVMV6>())
                c.IsConnected = connected.Contains(c);
        }
    }

    /// <summary>Removes the Contract connection matching a VM connection from the working blueprint.</summary>
    private void RemoveConnectionFromWorkingBlueprint(BlueprintConnectionVMV6 cvm)
    {
        if (_workingBlueprint == null) return;
        if (cvm.Source is not BlueprintConnectorVMV6 srcVm || cvm.Target is not BlueprintConnectorVMV6 tgtVm)
            return;
        if (!_connectorToContract.TryGetValue(srcVm, out var s) ||
            !_connectorToContract.TryGetValue(tgtVm, out var t))
            return;

        var bc = _workingBlueprint.Connections.FirstOrDefault(x =>
            x.SourceNodeId == s.NodeId && x.SourcePinId == s.PinId &&
            x.TargetNodeId == t.NodeId && x.TargetPinId == t.PinId);
        if (bc != null)
            _workingBlueprint.Connections.Remove(bc);
    }

    // ── Contract → ViewModel conversion ──

    private BlueprintNodeVMV6 ConvertNodeToViewModel(BlueprintNode node)
    {
        var functionName = (node as BuiltinFunctionNode)?.FunctionName;
        var headerColor = BlueprintNodeVMV6.GetHeaderColor(node.NodeType, functionName);
        var displayTitle = node.GetDisplayTitle();

        // Definition nodes (const/var block declarations) carry NO Exec pins — that is
        // the only reliable discriminator between a definition and a usage node.
        bool isDefinition = node is ConstNode or VariableNode
            && !node.InputPins.Any(p => p.Type == PinType.Execution)
            && !node.OutputPins.Any(p => p.Type == PinType.Execution);

        var nodeVm = new BlueprintNodeVMV6
        {
            BlueprintNodeId = node.Id,
            NodeType = node.NodeType,
            FunctionName = functionName,
            DisplayTitle = displayTitle,
            HeaderColorHex = headerColor,
            Comment = node.Comment,
            Title = displayTitle,
            IsDefinition = isDefinition,
            Location = new Avalonia.Point(node.X, node.Y),
        };

        foreach (var pin in node.InputPins)
        {
            var connector = new BlueprintConnectorVMV6(
                (connector, value) => UpdatePinDefaultValue(node.Id, connector.OriginalPinId, value))
            {
                Title = pin.Name,
                PinType = pin.Type,
                OriginalPinId = pin.Id,
                DefaultValue = pin.DefaultValue,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                IsDefinitionPin = isDefinition,
            };
            nodeVm.Input.Add(connector);
            RegisterConnector(connector, node.Id, pin.Id);
        }

        foreach (var pin in node.OutputPins)
        {
            var connector = new BlueprintConnectorVMV6
            {
                Title = pin.Name,
                PinType = pin.Type,
                OriginalPinId = pin.Id,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                IsDefinitionPin = isDefinition,
            };
            nodeVm.Output.Add(connector);
            RegisterConnector(connector, node.Id, pin.Id);
        }

        return nodeVm;
    }

    private void RegisterConnector(BlueprintConnectorVMV6 connector, string nodeId, string pinId)
    {
        connector.CanConnect = true;  // default connectable until hover-preview rejects
        _contractToConnector[(nodeId, pinId)] = connector;
        _connectorToContract[connector] = (nodeId, pinId);
    }

    /// <summary>
    /// Writes an edited inline default value back to the Contract pin (P5-A1). The
    /// working blueprint is the authoritative copy, so any later Reverse → KS
    /// round-trip picks the new literal up automatically.
    /// </summary>
    private void UpdatePinDefaultValue(string nodeId, string? pinId, string? value)
    {
        if (_workingBlueprint == null || string.IsNullOrEmpty(pinId)) return;
        var node = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == nodeId);
        var pin = node?.InputPins.Find(p => p.Id == pinId);
        if (pin != null)
            pin.DefaultValue = value;
    }

    private BlueprintConnectionVMV6? ConvertConnectionToViewModel(BlueprintConnection conn)
    {
        if (!_contractToConnector.TryGetValue((conn.SourceNodeId, conn.SourcePinId), out var source))
            return null;
        if (!_contractToConnector.TryGetValue((conn.TargetNodeId, conn.TargetPinId), out var target))
            return null;

        var connection = new BlueprintConnectionVMV6(this, source, target);
        source.IsConnected = true;
        target.IsConnected = true;
        return connection;
    }
}

/// <summary>A palette entry describing a creatable node.</summary>
public sealed record PaletteItemV6(string DisplayName, string Kind, string? FunctionName);

/// <summary>A dynamic palette entry for a plugin trigger (replaces the EntryNode on the canvas).</summary>
public sealed record PluginTriggerPaletteItemV6(string PluginName, string TriggerName)
{
    public string DisplayName => $"Trigger: {PluginName}.{TriggerName}";
}
