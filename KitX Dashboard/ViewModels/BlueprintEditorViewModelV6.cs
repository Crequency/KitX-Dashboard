using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
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

    /// <summary>Debounced scope-frame refresh after node moves (R6).</summary>
    private CancellationTokenSource? _scopeRefreshCts;

    /// <summary>Live group-comment notes, tracked for bounds following (R-fix).</summary>
    private readonly List<GroupCommentVM> _groupCommentVms = new();

    /// <summary>Group-comment note currently being dragged by the user (skip bounds-following).</summary>
    private GroupCommentVM? _draggingGroupComment;

    /// <summary>The single shared dashed-frame overlay (top Z-order, pure decoration).</summary>
    private GroupCommentHighlightVM? _highlightVm;

    /// <summary>The note currently hovered — drives the shared dashed frame.</summary>
    private GroupCommentVM? _hoveredGroupComment;

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
        Nodes.CollectionChanged += OnNodesCollectionChanged;
        PopulatePalette();
        RefreshPluginTriggers();
    }

    // ── Node move tracking (R6) ──

    /// <summary>
    /// Tracks node VMs entering/leaving the canvas so drags can write their new location
    /// back to the Contract and debounce-refresh the scope frames (which would otherwise
    /// never follow node movement, and saves would drop positions).
    /// </summary>
    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (var item in e.NewItems.OfType<BlueprintNodeVMV6>())
                item.PropertyChanged += OnNodeVmPropertyChanged;
        if (e.OldItems != null)
            foreach (var item in e.OldItems.OfType<BlueprintNodeVMV6>())
                item.PropertyChanged -= OnNodeVmPropertyChanged;
    }

    private void OnNodeVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BlueprintNodeVMV6.Location)) return;
        if (sender is not BlueprintNodeVMV6 node || _workingBlueprint == null) return;

        // Keep the authoritative Contract copy in sync with the canvas drag.
        var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == node.BlueprintNodeId);
        if (contractNode != null)
        {
            contractNode.X = node.Location.X;
            contractNode.Y = node.Location.Y;
        }
        ScheduleScopeRefresh();
    }

    /// <summary>Debounces scope-frame recalculation during drags (R6).</summary>
    private void ScheduleScopeRefresh()
    {
        _scopeRefreshCts?.Cancel();
        _scopeRefreshCts = new CancellationTokenSource();
        var token = _scopeRefreshCts.Token;
        _ = Task.Delay(200, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                RefreshScopes();
                UpdateGroupCommentBounds();
            });
        }, token);
    }

    // ── Group-comment notes: docking, magnetic snap, shared highlight (2026-08-02) ──

    /// <summary>Marks a note as being dragged so bounds-following skips it (code-behind calls this).</summary>
    public void SetGroupCommentDragging(GroupCommentVM? vm) => _draggingGroupComment = vm;

    /// <summary>
    /// Re-docks every note to its statement leader (only while docked; the note being
    /// dragged is skipped), then refreshes the shared dashed frame for the hovered note.
    /// Called from the node-move debounce.
    /// </summary>
    public void UpdateGroupCommentBounds()
    {
        if (_workingBlueprint == null) return;
        foreach (var vm in _groupCommentVms)
        {
            if (vm == _draggingGroupComment) continue;
            if (!vm.IsDocked) continue;
            DockNote(vm);
        }
        UpdateGroupCommentHighlight(_hoveredGroupComment);
    }

    /// <summary>Positions a docked note above its statement leader (primary node).</summary>
    private void DockNote(GroupCommentVM vm)
    {
        var anchor = _workingBlueprint?.Nodes.FirstOrDefault(n => n.Id == vm.AnchorNodeId);
        if (anchor == null) return;
        vm.Location = new Avalonia.Point(anchor.X, anchor.Y - 26);
    }

    /// <summary>
    /// Drives the SINGLE shared dashed frame: hovering a note positions the frame over
    /// that statement's data subgraph (NodeIds bounding box); null hides it (size zero —
    /// effectively absent, never conflicting with other controls).
    /// </summary>
    public void UpdateGroupCommentHighlight(GroupCommentVM? vm)
    {
        _hoveredGroupComment = vm;
        if (_highlightVm == null) return;
        if (vm == null || _workingBlueprint == null)
        {
            _highlightVm.Location = default;
            _highlightVm.Width = 0;
            _highlightVm.Height = 0;
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var id in vm.NodeIds)
        {
            var n = _workingBlueprint.Nodes.FirstOrDefault(x => x.Id == id);
            if (n == null) continue;
            minX = Math.Min(minX, n.X);
            minY = Math.Min(minY, n.Y);
            maxX = Math.Max(maxX, n.X + (n.Width > 0 ? n.Width : 180));
            maxY = Math.Max(maxY, n.Y + (n.Height > 0 ? n.Height : 60));
        }
        if (minX == double.MaxValue) return;

        _highlightVm.Location = new Avalonia.Point(minX, minY);
        _highlightVm.Width = maxX - minX;
        _highlightVm.Height = maxY - minY;
    }

    /// <summary>
    /// Magnetically snaps a note to a nearby statement *leader* node (KS→BP anchoring:
    /// the note sits above the statement's primary node). Only statement leaders
    /// (StatementPrimaryNodeIds) can carry a leading comment — otherwise the reverse
    /// translator would drop it; a leader already carrying a group comment rejects the
    /// snap (KS side allows at most one leading comment per statement). Dropping far
    /// from any leader leaves the note at a free position (undocked).
    /// </summary>
    public void SnapGroupCommentToNode(GroupCommentVM vm)
    {
        if (_workingBlueprint == null) return;

        var primaryIds = _workingBlueprint.StatementPrimaryNodeIds;
        var nearest = Nodes
            .OfType<BlueprintNodeVMV6>()
            .Where(n => !n.IsDefinition && primaryIds.Contains(n.BlueprintNodeId))
            .OrderBy(n => (n.Location.X - vm.Location.X) * (n.Location.X - vm.Location.X)
                        + (n.Location.Y - vm.Location.Y) * (n.Location.Y - vm.Location.Y))
            .FirstOrDefault();
        if (nearest == null) return;

        var distSq = (nearest.Location.X - vm.Location.X) * (nearest.Location.X - vm.Location.X)
                   + (nearest.Location.Y - vm.Location.Y) * (nearest.Location.Y - vm.Location.Y);
        if (distSq > 80 * 80)
        {
            // Far from any leader: free placement — node moves no longer pull the note.
            vm.IsDocked = false;
            vm.IsCollapsed = false;
            return;
        }

        if (_workingBlueprint.GroupComments.Any(gc => gc.AnchorNodeId == nearest.BlueprintNodeId
                                                       && gc.AnchorNodeId != vm.AnchorNodeId))
        {
            ErrorInfo = new ConstraintViolation("PRE", "Comment",
                "目标语句已有组注释（KS 侧一句话只能有一个 Leading 注释）。",
                new[] { nearest.BlueprintNodeId }, null, null, "#FF9800");
            return;
        }

        var contractComment = _workingBlueprint.GroupComments
            .FirstOrDefault(gc => gc.AnchorNodeId == vm.AnchorNodeId);
        if (contractComment != null)
        {
            contractComment.AnchorNodeId = nearest.BlueprintNodeId;
            contractComment.NodeIds = [nearest.BlueprintNodeId];
        }

        vm.AnchorNodeId = nearest.BlueprintNodeId;
        vm.NodeIds = [nearest.BlueprintNodeId];
        vm.IsDocked = true;
        DockNote(vm);
        ErrorInfo = null;
    }

    /// <summary>
    /// Creates a group comment on the BP canvas for the given node's statement (R-fix):
    /// adds the Contract BlueprintGroupComment + a note VM, then starts inline editing.
    /// </summary>
    [RelayCommand]
    private void AddGroupComment(BlueprintNodeVMV6? node)
    {
        if (node == null || _workingBlueprint == null || node.IsDefinition) return;
        if (_workingBlueprint.GroupComments.Any(gc => gc.AnchorNodeId == node.BlueprintNodeId))
        {
            ErrorInfo = new ConstraintViolation("PRE", "Comment",
                "该语句已有组注释（KS 侧一句话只能有一个 Leading 注释）。",
                new[] { node.BlueprintNodeId }, null, null, "#FF9800");
            return;
        }

        var contractComment = new BlueprintGroupComment
        {
            AnchorNodeId = node.BlueprintNodeId,
            NodeIds = [node.BlueprintNodeId],
            Comment = string.Empty,
        };
        _workingBlueprint.GroupComments.Add(contractComment);

        var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == node.BlueprintNodeId);
        var w = contractNode?.Width > 0 ? contractNode.Width : 160;
        var vm = new GroupCommentVM((v, text) => UpdateGroupCommentComment(v, text))
        {
            Comment = string.Empty,
            AnchorNodeId = node.BlueprintNodeId,
            NodeIds = [node.BlueprintNodeId],
            // Docked above the leader node; appended to Nodes → top Z-order.
            Location = new Avalonia.Point(node.Location.X, node.Location.Y - 26),
            Width = Math.Max(140, w),
            IsDocked = true,
        };
        _groupCommentVms.Add(vm);
        Nodes.Add(vm);
        vm.BeginEdit();
    }

    /// <summary>Writes an edited group-comment text back to the Contract (R-fix).</summary>
    private void UpdateGroupCommentComment(GroupCommentVM vm, string? text)
    {
        if (_workingBlueprint == null) return;
        var gc = _workingBlueprint.GroupComments.FirstOrDefault(x => x.AnchorNodeId == vm.AnchorNodeId);
        if (gc != null)
            gc.Comment = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
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

        // Phase 4: group-comment notes — placed AFTER the real nodes so they sit on top
        // of the Z-order (always hit-testable, even inside scope frames with dense nodes).
        _groupCommentVms.Clear();
        foreach (var groupComment in blueprint.GroupComments)
        {
            var anchor = blueprint.Nodes.FirstOrDefault(n => n.Id == groupComment.AnchorNodeId);
            if (anchor == null) continue;
            var vm = new GroupCommentVM((v, text) => UpdateGroupCommentComment(v, text))
            {
                Comment = groupComment.Comment,
                // Docked above the statement's primary (leader) node — KS→BP anchoring.
                Location = new Avalonia.Point(anchor.X, anchor.Y - 26),
                Width = Math.Max(140, anchor.Width),
                AnchorNodeId = groupComment.AnchorNodeId,
                NodeIds = groupComment.NodeIds.ToHashSet(),
                IsDocked = true,
            };
            _groupCommentVms.Add(vm);
            Nodes.Add(vm);
        }

        // Phase 5: the single shared dashed-frame overlay (top-most, pure decoration).
        _highlightVm = new GroupCommentHighlightVM();
        Nodes.Add(_highlightVm);

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
        _groupCommentVms.Clear();
        _highlightVm = null;
        _hoveredGroupComment = null;
        HasContent = false;
    }

    // ── Node palette (P3-β) ──

    /// <summary>Flat list of creatable nodes shown in the palette panel.</summary>
    public ObservableCollection<PaletteItemV6> PaletteItems { get; } = new();

    /// <summary>Palette search filter (R9).</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Control-flow palette items filtered by <see cref="SearchText"/> (R9).</summary>
    public ObservableCollection<PaletteItemV6> FilteredControlFlowItems { get; } = new();

    /// <summary>Builtin-function palette items filtered by <see cref="SearchText"/> (R9).</summary>
    public ObservableCollection<PaletteItemV6> FilteredBuiltinItems { get; } = new();

    partial void OnSearchTextChanged(string value) => RefreshFilteredPalette();

    /// <summary>Refreshes the grouped/filtered palette collections from <see cref="PaletteItems"/>.</summary>
    private void RefreshFilteredPalette()
    {
        var q = SearchText?.Trim();
        FilteredControlFlowItems.Clear();
        FilteredBuiltinItems.Clear();
        foreach (var item in PaletteItems)
        {
            if (!string.IsNullOrEmpty(q)
                && item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            (item.Kind == "ControlFlow" ? FilteredControlFlowItems : FilteredBuiltinItems).Add(item);
        }
    }

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
        RefreshFilteredPalette();
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
        RefreshScopes();
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
        TryExpandVariadicPins(src, tgt);
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

    /// <summary>
    /// Removes a single connection (Alt+click on a connector/connection, or the
    /// connection's context menu) — VM + Contract double-write (R3).
    /// </summary>
    [RelayCommand]
    private void RemoveConnection(BlueprintConnectionVMV6? connection)
    {
        if (connection == null || _workingBlueprint == null) return;
        Connections.Remove(connection);
        RemoveConnectionFromWorkingBlueprint(connection);
        RefreshIsConnected();
        RefreshScopes();
        ErrorInfo = null;
    }

    /// <summary>Deletes all selected nodes and their connections (canvas + Contract).</summary>
    [RelayCommand]
    private void DeleteSelectedNodes()
        => DeleteNodes(SelectedNodes.OfType<BlueprintNodeVMV6>().ToList());

    /// <summary>Deletes a single node (context menu).</summary>
    [RelayCommand]
    private void DeleteNode(BlueprintNodeVMV6? node)
    {
        if (node != null)
            DeleteNodes([node]);
    }

    private void DeleteNodes(List<BlueprintNodeVMV6> toRemove)
    {
        if (_workingBlueprint == null || toRemove.Count == 0) return;

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

        // Drop group comments anchored to any deleted node (P5-B2).
        if (_workingBlueprint.GroupComments.Count > 0)
            _workingBlueprint.GroupComments.RemoveAll(gc => nodeIds.Contains(gc.AnchorNodeId));

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

        // Source uniqueness (R4): a single output pin may only drive ONE target — the KS
        // side cannot express a single output fanning out to multiple consumers without a
        // variable (each reference becomes its own usage node), and exec stays linear.
        bool outputHasOutgoing = Connections.OfType<BlueprintConnectionVMV6>()
            .Any(c => c.Source == output);
        if (outputHasOutgoing)
            return new ConstraintViolation("PRE", "Outgoing",
                "输出端已有出边：KS 无法表达单输出多消费者（多路使用需经变量）。", highlight);

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

    // ── Variadic pin expansion (P5-C1) ──

    /// <summary>
    /// After a successful connect, auto-grows variadic pin groups (e.g. StringConcat's
    /// "Input N") when the last group pin gets connected — ported from v5.1
    /// TryExpandVariadicPins, adapted to the v6 double-write model (Contract + VM +
    /// both registries stay in sync). The appended pin round-trips through Reverse → KS.
    /// </summary>
    private void TryExpandVariadicPins(BlueprintConnectorVMV6 src, BlueprintConnectorVMV6 tgt)
    {
        // Input side grows when an input connector is the drop target (current builtins
        // only declare InputVariadic; output-side groups are deferred).
        TryExpandVariadicSide(tgt, isOutput: false);
    }

    private void TryExpandVariadicSide(BlueprintConnectorVMV6 connector, bool isOutput)
    {
        if (_workingBlueprint == null || _registry == null) return;
        if (!_connectorToContract.TryGetValue(connector, out var coord)) return;
        var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == coord.NodeId);
        if (contractNode is not BuiltinFunctionNode fn) return;
        var spec = _registry.Get(fn.FunctionName)?.InputVariadic;
        if (spec == null) return;

        var nodeVm = FindNodeById(fn.Id);
        if (nodeVm == null) return;

        // Legacy single-PinType mode: collect the node's pins belonging to the variadic
        // group (matching PinType). Expand only when the LAST pin was just connected.
        var pinList = nodeVm.Input;
        var pins = pinList.OfType<BlueprintConnectorVMV6>()
            .Where(c => c.PinType == spec.PinType)
            .ToList();
        if (pins.Count == 0 || connector != pins[^1]) return;

        // Derive the new pin's index from the static (base) group count vs current count.
        var baseCount = fn.InputPins.Count(p =>
            p.Name != "Exec"
            && p.Type == spec.PinType
            && (string.IsNullOrEmpty(spec.BasePinName)
                || !p.Name.StartsWith(spec.BasePinName, StringComparison.Ordinal)));
        var nextIndex = spec.StartIndex + (pins.Count - baseCount);
        var name = string.IsNullOrEmpty(spec.BasePinName)
            ? nextIndex.ToString()
            : $"{spec.BasePinName}{nextIndex}";

        // Append to Contract + VM + registries (v6 double-write).
        var newPin = new BlueprintPin
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Direction = PinDirection.Input,
            Type = spec.PinType,
        };
        fn.InputPins.Add(newPin);

        var connectorVm = new BlueprintConnectorVMV6(
            (c, value) => UpdatePinDefaultValue(fn.Id, c.OriginalPinId, value))
        {
            Title = name,
            PinType = spec.PinType,
            OriginalPinId = newPin.Id,
            Flow = ConnectorViewModelBase.ConnectorFlow.Input,
            IsDefinitionPin = false,
        };
        nodeVm.Input.Add(connectorVm);
        RegisterConnector(connectorVm, fn.Id, newPin.Id);
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

        var nodeVm = new BlueprintNodeVMV6(
            (node, comment) => UpdateNodeComment(node.BlueprintNodeId, comment),
            (node, oldName, newName, value) => UpdateDefinitionNode(node, oldName, newName, value))
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

        // Definition nodes carry editable declaration name/type/value (R8).
        if (isDefinition)
        {
            switch (node)
            {
                case ConstNode cn:
                    nodeVm.DefinitionName = cn.ConstName;
                    nodeVm.DefinitionType = cn.ConstType;
                    nodeVm.DefinitionValue = cn.ConstValue;
                    break;
                case VariableNode vn:
                    nodeVm.DefinitionName = vn.VarName;
                    nodeVm.DefinitionType = vn.VarType;
                    nodeVm.DefinitionValue = vn.VarInitialValue;
                    break;
            }
        }

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

    /// <summary>Writes an edited node comment back to the Contract node (P5-B2, trailing comment).</summary>
    private void UpdateNodeComment(string nodeId, string? comment)
    {
        if (_workingBlueprint == null) return;
        var node = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node != null)
            node.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment;
    }

    /// <summary>
    /// Writes an edited definition node's declaration name/value back to the Contract (R8).
    /// Renaming a definition node synchronises all same-named usage nodes in the working
    /// blueprint so Reverse produces a consistent declaration + references.
    /// </summary>
    private void UpdateDefinitionNode(BlueprintNodeVMV6 node, string? oldName, string? newName, string? value)
    {
        if (_workingBlueprint == null) return;
        var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == node.BlueprintNodeId);
        switch (contractNode)
        {
            case ConstNode cn:
                if (!string.IsNullOrEmpty(newName))
                    cn.ConstName = newName;
                cn.ConstValue = string.IsNullOrWhiteSpace(value) ? null : value;
                break;
            case VariableNode vn:
                if (!string.IsNullOrEmpty(newName) && newName != oldName)
                {
                    vn.VarName = newName;
                    foreach (var other in _workingBlueprint.Nodes.OfType<VariableNode>())
                        if (!ReferenceEquals(other, vn) && other.VarName == oldName)
                            other.VarName = newName;
                }
                vn.VarInitialValue = string.IsNullOrWhiteSpace(value) ? null : value;
                break;
        }
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
