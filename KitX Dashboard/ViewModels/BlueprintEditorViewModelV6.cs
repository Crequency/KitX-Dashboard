using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
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

    /// <summary>
    /// Raised after any edit that changes the persisted workflow content (structure,
    /// comments, definition values). The host VM subscribes to mark the workflow dirty —
    /// without it, BP-side edits would be silently dropped on window close (the IsDirty
    /// pipeline only covered KS-side setters). Pure canvas moves are excluded: positions
    /// are decorative and not persisted.
    /// </summary>
    public event Action? BlueprintEdited;

    private void NotifyBlueprintEdited() => BlueprintEdited?.Invoke();

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

    // ── Declared const/var names (usage-node ComboBox source, 2026-08-03) ──

    /// <summary>
    /// All declared variable/constant names on the canvas (definition ConstNode/VariableNode
    /// names + Each loop ItemName). Backs the usage-VariableNode's name ComboBox; refreshed
    /// whenever definitions change (load / add / rename / delete).
    /// </summary>
    public ObservableCollection<string> DefinitionNames { get; } = new();

    /// <summary>Rebuilds <see cref="DefinitionNames"/> from the working blueprint's definition nodes.</summary>
    public void RefreshDefinitionNames()
    {
        DefinitionNames.Clear();
        if (_workingBlueprint == null) return;
        foreach (var node in _workingBlueprint.Nodes)
        {
            switch (node)
            {
                case ConstNode cn when !string.IsNullOrEmpty(cn.ConstName):
                    DefinitionNames.Add(cn.ConstName);
                    break;
                case VariableNode vn when vn.IsDefinition && !string.IsNullOrEmpty(vn.VarName):
                    DefinitionNames.Add(vn.VarName);
                    break;
                case BuiltinFunctionNode fn when fn.FunctionName == "Each"
                                                 && fn.Properties.TryGetValue("ItemName", out var item)
                                                 && !string.IsNullOrEmpty(item):
                    DefinitionNames.Add(item);
                    break;
            }
        }
    }

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

    /// <summary>Re-evaluates theme-dependent connector/line colours after a theme switch.</summary>
    public void RefreshThemeColors()
    {
        foreach (var nodeVm in Nodes.OfType<BlueprintNodeVMV6>())
        {
            foreach (var c in nodeVm.Input.OfType<BlueprintConnectorVMV6>())
                c.RefreshThemeColor();
            foreach (var c in nodeVm.Output.OfType<BlueprintConnectorVMV6>())
                c.RefreshThemeColor();
        }
        foreach (var c in Connections.OfType<BlueprintConnectionVMV6>())
            c.RefreshStrokeColor();
    }

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
    /// Magnetically snaps a note to a nearby data node. The note lands on the *containing
    /// statement's* primary (leader) node — any data node can receive the snap, it is
    /// mapped to its data subgraph's primary via the backend's StatementNodeToPrimary
    /// (KS→BP anchoring). A statement already carrying a group comment rejects the snap
    /// (KS side allows at most one leading comment per statement). Dropping far from any
    /// node leaves the note at a free position (undocked).
    /// </summary>
    public void SnapGroupCommentToNode(GroupCommentVM vm)
    {
        if (_workingBlueprint == null) return;

        // 1. Nearest non-definition node (any data node can host a snap).
        var nearest = Nodes
            .OfType<BlueprintNodeVMV6>()
            .Where(n => !n.IsDefinition)
            .OrderBy(n => (n.Location.X - vm.Location.X) * (n.Location.X - vm.Location.X)
                        + (n.Location.Y - vm.Location.Y) * (n.Location.Y - vm.Location.Y))
            .FirstOrDefault();
        if (nearest == null) return;

        var distSq = (nearest.Location.X - vm.Location.X) * (nearest.Location.X - vm.Location.X)
                   + (nearest.Location.Y - vm.Location.Y) * (nearest.Location.Y - vm.Location.Y);
        if (distSq > 80 * 80)
        {
            // Far from any node: free placement — node moves no longer pull the note.
            vm.IsDocked = false;
            vm.IsCollapsed = false;
            return;
        }

        // 2. Resolve the containing statement's primary (leader) via the backend mapping.
        var primaryId = _workingBlueprint.StatementNodeToPrimary.TryGetValue(nearest.BlueprintNodeId, out var pid)
            ? pid
            : nearest.BlueprintNodeId;

        // 3. Conflict: that statement already carries a group comment (KS 1:1).
        if (_workingBlueprint.GroupComments.Any(gc => gc.AnchorNodeId == primaryId
                                                       && gc.AnchorNodeId != vm.AnchorNodeId))
        {
            ErrorInfo = new ConstraintViolation("PRE", "Comment",
                "目标语句已有组注释（KS 侧一句话只能有一个 Leading 注释）。",
                new[] { primaryId }, null, null, "#FF9800");
            return;
        }

        // 4. Re-anchor: update the Contract GroupComment + the note's subgraph (all nodes
        //    of the containing statement's data subgraph).
        var subgraphNodes = _workingBlueprint.StatementNodeToPrimary
            .Where(kvp => kvp.Value == primaryId)
            .Select(kvp => kvp.Key)
            .ToList();
        if (subgraphNodes.Count == 0)
            subgraphNodes = [primaryId];

        var contractComment = _workingBlueprint.GroupComments
            .FirstOrDefault(gc => gc.AnchorNodeId == vm.AnchorNodeId);
        if (contractComment != null)
        {
            contractComment.AnchorNodeId = primaryId;
            contractComment.NodeIds = subgraphNodes;
        }

        vm.AnchorNodeId = primaryId;
        vm.NodeIds = subgraphNodes.ToHashSet();
        vm.IsDocked = true;
        DockNote(vm);
        ErrorInfo = null;
        NotifyBlueprintEdited();
    }

    /// <summary>
    /// Creates a group comment on the BP canvas for the given node's statement (R-fix):
    /// adds the Contract BlueprintGroupComment + a note VM, then starts inline editing.
    /// </summary>
    [RelayCommand]
    private void AddGroupComment(BlueprintNodeVMV6? node)
    {
        if (node == null || _workingBlueprint == null || node.IsDefinition) return;

        // The comment anchors to the containing statement's primary (leader) node — the
        // backend maps every data node to its statement's primary.
        var primaryId = _workingBlueprint.StatementNodeToPrimary.TryGetValue(node.BlueprintNodeId, out var pid)
            ? pid
            : node.BlueprintNodeId;
        if (_workingBlueprint.GroupComments.Any(gc => gc.AnchorNodeId == primaryId))
        {
            ErrorInfo = new ConstraintViolation("PRE", "Comment",
                "该语句已有组注释（KS 侧一句话只能有一个 Leading 注释）。",
                new[] { primaryId }, null, null, "#FF9800");
            return;
        }

        var subgraphNodes = _workingBlueprint.StatementNodeToPrimary
            .Where(kvp => kvp.Value == primaryId)
            .Select(kvp => kvp.Key)
            .ToList();
        if (subgraphNodes.Count == 0)
            subgraphNodes = [primaryId];

        var contractComment = new BlueprintGroupComment
        {
            AnchorNodeId = primaryId,
            NodeIds = subgraphNodes,
            Comment = string.Empty,
        };
        _workingBlueprint.GroupComments.Add(contractComment);

        var primaryVm = FindNodeById(primaryId) ?? node;
        var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == primaryId);
        var w = contractNode?.Width > 0 ? contractNode.Width : 160;
        var vm = new GroupCommentVM((v, text) => UpdateGroupCommentComment(v, text))
        {
            Comment = string.Empty,
            AnchorNodeId = primaryId,
            NodeIds = subgraphNodes.ToHashSet(),
            // Docked above the leader node; appended to Nodes → top Z-order.
            Location = new Avalonia.Point(primaryVm.Location.X, primaryVm.Location.Y - 26),
            Width = Math.Max(140, w),
            IsDocked = true,
        };
        _groupCommentVms.Add(vm);
        Nodes.Add(vm);
        vm.BeginEdit();
        NotifyBlueprintEdited();
    }

    /// <summary>Writes an edited group-comment text back to the Contract (R-fix).</summary>
    private void UpdateGroupCommentComment(GroupCommentVM vm, string? text)
    {
        if (_workingBlueprint == null) return;
        var gc = _workingBlueprint.GroupComments.FirstOrDefault(x => x.AnchorNodeId == vm.AnchorNodeId);
        if (gc != null)
            gc.Comment = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
        NotifyBlueprintEdited();
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
        RefreshDefinitionNames();
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

    /// <summary>Definition-node palette items (const/var block declarations) filtered by <see cref="SearchText"/>.</summary>
    public ObservableCollection<PaletteItemV6> FilteredDefinitionItems { get; } = new();

    /// <summary>Usage-node palette items (literal const / variable reference) filtered by <see cref="SearchText"/>.</summary>
    public ObservableCollection<PaletteItemV6> FilteredUsageItems { get; } = new();

    partial void OnSearchTextChanged(string value) => RefreshFilteredPalette();

    /// <summary>Refreshes the grouped/filtered palette collections from <see cref="PaletteItems"/>.</summary>
    private void RefreshFilteredPalette()
    {
        var q = SearchText?.Trim();
        FilteredControlFlowItems.Clear();
        FilteredBuiltinItems.Clear();
        FilteredDefinitionItems.Clear();
        FilteredUsageItems.Clear();
        foreach (var item in PaletteItems)
        {
            if (!string.IsNullOrEmpty(q)
                && item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            var target = item.Kind switch
            {
                "ControlFlow" => FilteredControlFlowItems,
                "Definition" => FilteredDefinitionItems,
                "Usage" => FilteredUsageItems,
                _ => FilteredBuiltinItems,
            };
            target.Add(item);
        }
    }

    /// <summary>Fills the palette from the builtin registry + hardcoded control-flow/definition/usage set.</summary>
    private void PopulatePalette()
    {
        PaletteItems.Clear();
        PaletteItems.Add(new PaletteItemV6("if (Branch)", "ControlFlow", "Branch"));
        PaletteItems.Add(new PaletteItemV6("forEach (Each)", "ControlFlow", "Each"));
        PaletteItems.Add(new PaletteItemV6("while (While)", "ControlFlow", "While"));
        PaletteItems.Add(new PaletteItemV6("switch (Switch)", "ControlFlow", "Switch"));
        PaletteItems.Add(new PaletteItemV6("break", "ControlFlow", "break"));
        PaletteItems.Add(new PaletteItemV6("continue", "ControlFlow", "continue"));
        // Const/var nodes (2026-08-03): definition declarations + usage references.
        PaletteItems.Add(new PaletteItemV6("const（定义）", "Definition", "const"));
        PaletteItems.Add(new PaletteItemV6("var（定义）", "Definition", "var"));
        PaletteItems.Add(new PaletteItemV6("常量（字面量）", "Usage", "literal"));
        PaletteItems.Add(new PaletteItemV6("变量（使用）", "Usage", "var"));
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
            "Definition" => item.FunctionName switch
            {
                "const" => NodeFactoryV6.CreateConstDefinitionNode(),
                "var" => NodeFactoryV6.CreateVariableDefinitionNode(),
                _ => null!,
            },
            "Usage" => item.FunctionName switch
            {
                "literal" => NodeFactoryV6.CreateConstUsageNode(),
                "var" => NodeFactoryV6.CreateVariableUsageNode(),
                _ => null!,
            },
            "Builtin" when _registry != null
                => NodeFactoryV6.CreateBuiltinFunctionNode(item.FunctionName!, _registry),
            _ => null!,
        };
        if (node == null) return;
        AddNodeToCanvas(node);
        RefreshDefinitionNames();
    }

    private int _nodeCounter;

    private void AddNodeToCanvas(BlueprintNode node)
    {
        var x = 200 + (_nodeCounter % 5) * 230;
        var y = 120 + (_nodeCounter / 5) * 140;
        _nodeCounter++;
        AddNodeToCanvas(node, x, y);
    }

    /// <summary>Adds a node to canvas + working blueprint at an explicit canvas position.</summary>
    private void AddNodeToCanvas(BlueprintNode node, double x, double y)
    {
        if (_workingBlueprint == null) return;
        node.X = x;
        node.Y = y;
        _workingBlueprint.Nodes.Add(node);
        Nodes.Add(ConvertNodeToViewModel(node));
        HasContent = true;
        RefreshScopes();
        NotifyBlueprintEdited();
    }

    // ── Node selector popup (drag-from-output → create node in place) ──

    /// <summary>True while the in-place node selector popup is open.</summary>
    [ObservableProperty]
    private bool _isNodeSelectorOpen;

    /// <summary>Search text filtering the selector list.</summary>
    [ObservableProperty]
    private string _nodeSelectorSearchText = string.Empty;

    /// <summary>Nodes compatible with the dragged source pin, filtered by <see cref="NodeSelectorSearchText"/>.</summary>
    public ObservableCollection<PaletteItemV6> NodeSelectorItems { get; } = new();

    /// <summary>The full compatible list (pre-filter), refreshed by search.</summary>
    private readonly List<PaletteItemV6> _nodeSelectorAll = new();

    private BlueprintConnectorVMV6? _selectorSource;
    private Avalonia.Point _selectorDropPosition;

    partial void OnNodeSelectorSearchTextChanged(string value) => RefreshNodeSelectorItems();

    /// <summary>
    /// Opens the in-place node selector for a drag released on blank canvas. Only
    /// OUTPUT-port drags open it (design decision: input-port drags never create
    /// nodes — replacing a source would require re-wiring the consumer chain).
    /// Items are filtered to nodes that can actually accept the dragged value.
    /// </summary>
    public void OpenNodeSelector(BlueprintConnectorVMV6 source, Avalonia.Point canvasPos)
    {
        if (source.Flow != ConnectorViewModelBase.ConnectorFlow.Output) return;
        if (_workingBlueprint == null || _registry == null) return;
        _selectorSource = source;
        _selectorDropPosition = canvasPos;
        _nodeSelectorAll.Clear();
        foreach (var item in PaletteItems)
            if (IsSelectorCompatible(source, item))
                _nodeSelectorAll.Add(item);
        NodeSelectorSearchText = string.Empty;
        RefreshNodeSelectorItems();
        IsNodeSelectorOpen = true;
    }

    /// <summary>Closes the selector and clears its transient state.</summary>
    public void CloseNodeSelector()
    {
        IsNodeSelectorOpen = false;
        _selectorSource = null;
        _nodeSelectorAll.Clear();
        NodeSelectorItems.Clear();
        NodeSelectorSearchText = string.Empty;
    }

    private void RefreshNodeSelectorItems()
    {
        NodeSelectorItems.Clear();
        var q = NodeSelectorSearchText?.Trim();
        foreach (var item in _nodeSelectorAll)
        {
            if (!string.IsNullOrEmpty(q)
                && item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            NodeSelectorItems.Add(item);
        }
    }

    /// <summary>
    /// Whether a palette node can accept a connection from the dragged source pin:
    /// the node must expose a compatible input pin (Exec→Exec; data→data with
    /// exact-type-or-Any compatibility). Probed on a throwaway NodeFactoryV6 instance
    /// that never enters the canvas.
    /// </summary>
    private bool IsSelectorCompatible(BlueprintConnectorVMV6 source, PaletteItemV6 item)
    {
        BlueprintNode node;
        try
        {
            node = item.Kind switch
            {
                "ControlFlow" => NodeFactoryV6.CreateControlFlowNode(item.FunctionName!),
                "Builtin" => NodeFactoryV6.CreateBuiltinFunctionNode(item.FunctionName!, _registry!),
                _ => null!,
            };
        }
        catch
        {
            return false;
        }
        if (node == null) return false;

        return source.IsExecution
            ? node.InputPins.Any(p => p.Type == PinType.Execution)
            : node.InputPins.Any(p => p.Type != PinType.Execution
                && (p.Type == PinType.Any || p.Type == source.PinType));
    }

    /// <summary>
    /// Creates the selected node at the drop position and auto-wires it to the dragged
    /// source: exec edges first (the new node joins the main exec chain via insertion),
    /// then the data edge (which would otherwise be rejected as cross-region while the
    /// node is still exec-unreachable).
    /// </summary>
    [RelayCommand]
    private void SelectNodeSelectorItem(PaletteItemV6? item)
    {
        var source = _selectorSource;
        var drop = _selectorDropPosition;
        CloseNodeSelector();
        if (item == null || source == null || _workingBlueprint == null || _registry == null)
            return;

        BlueprintNode node = item.Kind switch
        {
            "ControlFlow" => NodeFactoryV6.CreateControlFlowNode(item.FunctionName!),
            "Builtin" => NodeFactoryV6.CreateBuiltinFunctionNode(item.FunctionName!, _registry),
            _ => null!,
        };
        if (node == null) return;

        AddNodeToCanvas(node, drop.X, drop.Y);
        var nodeVm = FindNodeById(node.Id);
        if (nodeVm == null) return;

        var newExecIn = nodeVm.Input.OfType<BlueprintConnectorVMV6>()
            .FirstOrDefault(c => c.IsExecution);

        if (source.IsExecution)
        {
            // Exec source: insert the new node right after the source (chain insertion
            // keeps the source's old successor — nothing gets orphaned).
            if (newExecIn != null)
                InsertIntoExecChain(source, newExecIn);
            return;
        }

        // Data source: join the exec chain along the data-flow direction — the source
        // node's Exec output feeds the new node (inserted mid-chain when occupied).
        var sourceNodeId = FindNodeIdForConnector(source);
        var sourceExecOut = sourceNodeId is null ? null
            : FindNodeById(sourceNodeId)?.Output.OfType<BlueprintConnectorVMV6>()
                .FirstOrDefault(c => c.IsExecution);
        if (newExecIn != null && sourceExecOut != null)
            InsertIntoExecChain(sourceExecOut, newExecIn);

        // Then the data edge (Any-typed input preferred over exact-type pins so e.g.
        // Compare receives a value on A/B rather than overwriting its Op literal).
        var dataPin = nodeVm.Input.OfType<BlueprintConnectorVMV6>()
            .FirstOrDefault(c => !c.IsExecution && c.PinType == PinType.Any)
            ?? nodeVm.Input.OfType<BlueprintConnectorVMV6>()
                .FirstOrDefault(c => !c.IsExecution && c.PinType == source.PinType);
        if (dataPin != null)
            Connect(source, dataPin);
    }

    /// <summary>
    /// Inserts a node between <paramref name="execOut"/> and its current exec successor
    /// (or appends it when the output is free). The old successor is re-attached to the
    /// new node, so no sub-graph is ever orphaned by this operation.
    /// </summary>
    private void InsertIntoExecChain(BlueprintConnectorVMV6 execOut, BlueprintConnectorVMV6 newExecIn)
    {
        var oldEdge = Connections.OfType<BlueprintConnectionVMV6>()
            .FirstOrDefault(c => c.Source == execOut);
        if (oldEdge == null)
        {
            Connect(execOut, newExecIn);
            return;
        }

        Connections.Remove(oldEdge);
        RemoveConnectionFromWorkingBlueprint(oldEdge);
        Connect(execOut, newExecIn);

        var newNodeId = FindNodeIdForConnector(newExecIn);
        var newNodeExecOut = newNodeId is null ? null
            : FindNodeById(newNodeId)?.Output.OfType<BlueprintConnectorVMV6>()
                .FirstOrDefault(c => c.IsExecution);
        if (newNodeExecOut != null)
            Connect(newNodeExecOut, oldEdge.Target);
        // Both connects are structurally guaranteed to succeed (the new node's exec
        // input is free; the old successor's exec input was just released), so a
        // failure would leave the graph in a legal mid-state the user can repair.
    }

    /// <summary>Connector → owning node id via the reverse lookup table.</summary>
    private string? FindNodeIdForConnector(BlueprintConnectorVMV6 connector)
        => _connectorToContract.TryGetValue(connector, out var coord) ? coord.NodeId : null;

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

        // In-place entry swap (single entry guarantee) — see TriggerEntrySwapper.
        if (TriggerEntrySwapper.SwapToPlugin(_workingBlueprint, item.PluginName, item.TriggerName) is null)
        {
            AddNodeToCanvas(node);
        }

        // Rebuild the canvas from the Contract so VM connectors match the new root.
        ReloadCanvas();
        NotifyBlueprintEdited();
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
    ///
    /// Reroute semantics (output port already occupied): "respect the user's latest
    /// operation" — the source output's existing edge is disconnected first, then the
    /// new edge is validated and committed. When the rerouted edge is an EXEC edge,
    /// the old chain tail becomes a preserved detached graph (BP-side privilege, see
    /// Workflow.DetachedGraphs) and a warning is surfaced. On validation failure the
    /// disconnected edge is restored.
    /// </summary>
    public override void Connect(ConnectorViewModelBase source, ConnectorViewModelBase target)
    {
        if (source is not BlueprintConnectorVMV6 src || target is not BlueprintConnectorVMV6 tgt)
            return;
        if (_workingBlueprint == null || _bpGraphLens == null)
            return;

        // 1. Frontend pre-check (fast, pin-precise). A busy source output no longer
        //    rejects — the existing edge is re-routed below.
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

        // 3. Reroute: disconnect the source output's existing edge (at most one by the
        //    single-consumer invariant). An EXEC reroute orphans the old chain tail —
        //    preserved as a detached graph (BP privilege) and surfaced as a warning.
        var oldEdge = Connections.OfType<BlueprintConnectionVMV6>()
            .FirstOrDefault(c => c.Source == outVm);
        bool execRerouted = false;
        string? detachedTailId = null;
        if (oldEdge != null)
        {
            Connections.Remove(oldEdge);
            RemoveConnectionFromWorkingBlueprint(oldEdge);
            RefreshIsConnected();
            if (outVm.IsExecution
                && oldEdge.Target is BlueprintConnectorVMV6 oldTargetVm
                && _connectorToContract.TryGetValue(oldTargetVm, out var oldTgtCoord))
            {
                execRerouted = true;
                detachedTailId = oldTgtCoord.NodeId;
            }
        }

        // 4. Add candidate edge to the working blueprint, then validate the full graph.
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
            // Roll back the reroute so the graph stays in its previous (legal) state.
            if (oldEdge != null)
                RestoreConnection(oldEdge);
            ErrorInfo = violation;
            return;
        }

        // 5. Commit: create the VM connection + refresh IsConnected + scopes.
        var connVm = new BlueprintConnectionVMV6(this, outVm, inVm);
        Connections.Add(connVm);
        outVm.IsConnected = true;
        inVm.IsConnected = true;
        ErrorInfo = null;

        // 6. Exec-reroute warning: the old chain tail is now a preserved detached graph
        //    (unless it is still data-proxy reachable from the main chain).
        if (execRerouted && detachedTailId is not null && !IsReachable(detachedTailId))
            ErrorInfo = new ConstraintViolation("WARN", "Detach",
                $"已断开节点 '{detachedTailId}' 的执行连线：其执行后继已脱离主执行流，将保留为孤立子图（BP 特权，KS 不可见）。可从主链拖线接回。",
                new[] { detachedTailId }, null,
                "如需保持执行顺序，请从输出端口拖出并选择新节点（自动链插入）。", "#FF9800");

        TryExpandVariadicPins(src, tgt);
        RefreshScopes();
        NotifyBlueprintEdited();
    }

    /// <summary>Re-commits a previously disconnected VM connection (VM + Contract double-write).</summary>
    private void RestoreConnection(BlueprintConnectionVMV6 cvm)
    {
        if (_workingBlueprint == null) return;
        if (cvm.Source is not BlueprintConnectorVMV6 s || cvm.Target is not BlueprintConnectorVMV6 t) return;
        if (!_connectorToContract.TryGetValue(s, out var sc) ||
            !_connectorToContract.TryGetValue(t, out var tc)) return;
        _workingBlueprint.Connections.Add(new BlueprintConnection
        {
            Id = Guid.NewGuid().ToString(),
            SourceNodeId = sc.NodeId,
            SourcePinId = sc.PinId,
            TargetNodeId = tc.NodeId,
            TargetPinId = tc.PinId,
        });
        Connections.Add(cvm);
        s.IsConnected = true;
        t.IsConnected = true;
    }

    /// <summary>
    /// True when the node is reachable from the Entry/PluginTrigger root via exec edges
    /// or data-proxy edges (mirrors StructuralReducer's E1 reachability semantics).
    /// Used by the detached-graph warning and the cross-region data-edge guard.
    /// </summary>
    private bool IsReachable(string nodeId) => ComputeReachableIds().Contains(nodeId);

    /// <summary>Computes the set of nodes reachable from the exec root (exec + data-proxy BFS).</summary>
    private HashSet<string> ComputeReachableIds()
    {
        var reachable = new HashSet<string>();
        if (_workingBlueprint == null) return reachable;
        var nodes = _workingBlueprint.Nodes;
        var entry = nodes.FirstOrDefault(n => n is EntryNode or PluginTriggerNode);
        if (entry == null) return reachable;

        var queue = new Queue<string>();
        queue.Enqueue(entry.Id);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!reachable.Add(id)) continue;
            var node = nodes.FirstOrDefault(n => n.Id == id);
            if (node == null) continue;
            foreach (var outPin in node.OutputPins)
            {
                if (outPin.Type != PinType.Execution) continue;
                foreach (var conn in _workingBlueprint.Connections)
                    if (conn.SourceNodeId == id && conn.SourcePinId == outPin.Id)
                        queue.Enqueue(conn.TargetNodeId);
            }
        }

        // Data-proxy reachability (StructuralReducer E1): nodes fed by data edges from
        // reachable nodes are themselves reachable.
        var data = new HashSet<string>();
        var dq = new Queue<string>();
        foreach (var rid in reachable)
        {
            var node = nodes.FirstOrDefault(n => n.Id == rid);
            if (node == null) continue;
            foreach (var inPin in node.InputPins)
            {
                if (inPin.Type == PinType.Execution) continue;
                foreach (var conn in _workingBlueprint.Connections)
                    if (conn.TargetNodeId == rid && conn.TargetPinId == inPin.Id && data.Add(conn.SourceNodeId))
                        dq.Enqueue(conn.SourceNodeId);
            }
        }
        while (dq.Count > 0)
        {
            var id = dq.Dequeue();
            var node = nodes.FirstOrDefault(n => n.Id == id);
            if (node == null) continue;
            foreach (var inPin in node.InputPins)
            {
                if (inPin.Type == PinType.Execution) continue;
                foreach (var conn in _workingBlueprint.Connections)
                    if (conn.TargetNodeId == id && conn.TargetPinId == inPin.Id && data.Add(conn.SourceNodeId))
                        dq.Enqueue(conn.SourceNodeId);
            }
        }
        reachable.UnionWith(data);
        return reachable;
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
        NotifyBlueprintEdited();
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
        NotifyBlueprintEdited();
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

        // Drop group comments anchored to any deleted node (P5-B2): Contract entries AND
        // their live note VMs + the shared highlight (an orphaned note would otherwise
        // float over the canvas after its anchor is deleted).
        if (_workingBlueprint.GroupComments.Count > 0 || _groupCommentVms.Count > 0)
        {
            _workingBlueprint.GroupComments.RemoveAll(gc => nodeIds.Contains(gc.AnchorNodeId));
            var orphanedNotes = _groupCommentVms
                .Where(vm => nodeIds.Contains(vm.AnchorNodeId))
                .ToList();
            foreach (var vm in orphanedNotes)
            {
                _groupCommentVms.Remove(vm);
                Nodes.Remove(vm);
            }
            if (_hoveredGroupComment != null && nodeIds.Contains(_hoveredGroupComment.AnchorNodeId))
            {
                _hoveredGroupComment = null;
                UpdateGroupCommentHighlight(null);
            }
        }

        SelectedNodes.Clear();
        RefreshIsConnected();
        RefreshScopes();
        ErrorInfo = null;
        RefreshDefinitionNames();
        NotifyBlueprintEdited();
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

        // Cross-region data edges (detached-graph privilege): a data edge between the
        // main exec chain and a detached (exec-unreachable) sub-graph cannot be
        // expressed — the detached graph never executes, so the value would never flow,
        // and Reverse would silently drop one endpoint's wiring. Both endpoints must be
        // on the same side of the reachability boundary. (Exec edges are unaffected.)
        if (!output.IsExecution)
        {
            var reachable = ComputeReachableIds();
            if (reachable.Contains(srcCoord.NodeId) != reachable.Contains(tgtCoord.NodeId))
                return new ConstraintViolation("PRE", "Region",
                    "跨区数据连线不允许：孤立子图与主执行链之间的数据引用无法表达（孤立图不执行，值不会流动）。", highlight);
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

    // Connection-structural classification lives on the backend ConstraintViolation
    // (IsConnectionStructural) — the frontend never hardcodes the code set, so adding
    // new rejection codes cannot silently misclassify edits.
    private static bool IsConnectionStructural(ConstraintViolation? v)
        => v?.IsConnectionStructural == true;

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

        // Reroute notice: the source output already has an outgoing edge — dropping
        // here will disconnect it (respect-the-latest-operation semantics).
        if (src.Flow == ConnectorViewModelBase.ConnectorFlow.Output)
        {
            if (Connections.OfType<BlueprintConnectionVMV6>().Any(c => c.Source == src))
                HoverErrorText = "该输出已有连接：释放后将断开旧连接（重路由）。";
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

        // Definition nodes (const/var block declarations) are flagged by the renderer
        // (BpRenderer sets IsDefinition on /def/ nodes) — definition-ness is fixed at
        // creation and must not be re-inferred from pin presence or connectivity.
        bool isDefinition = node switch
        {
            ConstNode cn => cn.IsDefinition,
            VariableNode vn => vn.IsDefinition,
            _ => false,
        };

        var nodeVm = new BlueprintNodeVMV6(
            (node, comment) => UpdateNodeComment(node.BlueprintNodeId, comment),
            (node, oldName, newName, value) => UpdateDefinitionNode(node, oldName, newName, value),
            (node, name) => UpdateUsageNodeName(node, name),
            (node, value) => UpdateUsageConstValue(node, value))
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
        // ApplyDefinitionFromContract assigns fields directly (no edit callbacks) so
        // loading can never write uninitialised VM state back over the Contract.
        if (isDefinition)
        {
            switch (node)
            {
                case ConstNode cn:
                    nodeVm.ApplyDefinitionFromContract(cn.ConstName, cn.ConstType, cn.DefaultValue, cn.ConstValue);
                    Log.Information("[BPEditVM] ConvertNode definition const: id={Id} name={Name} default={Def} user={User}",
                        node.Id, cn.ConstName, cn.DefaultValue, cn.ConstValue);
                    break;
                case VariableNode vn:
                    nodeVm.ApplyDefinitionFromContract(vn.VarName, vn.VarType, vn.DefaultValue, vn.VarInitialValue);
                    Log.Information("[BPEditVM] ConvertNode definition var: id={Id} name={Name} default={Def} user={User}",
                        node.Id, vn.VarName, vn.DefaultValue, vn.VarInitialValue);
                    break;
            }
        }
        else
        {
            // Usage nodes (2026-08-03): VariableNode → editable referenced name;
            // ConstNode (literal) → editable literal value. Direct field assignment,
            // no edit callbacks (same load-safety rationale as the definition path).
            switch (node)
            {
                case VariableNode vn:
                    nodeVm.ApplyUsageFromContract(vn.VarName, null);
                    break;
                case ConstNode cn:
                    nodeVm.ApplyUsageFromContract(null, cn.ConstValue);
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
        NotifyBlueprintEdited();
    }

    /// <summary>Writes an edited node comment back to the Contract node (P5-B2, trailing comment).</summary>
    private void UpdateNodeComment(string nodeId, string? comment)
    {
        if (_workingBlueprint == null) return;
        var node = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node != null)
            node.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment;
        NotifyBlueprintEdited();
    }

    /// <summary>
    /// Writes an edited usage-VariableNode's referenced name back to the Contract
    /// (2026-08-03). Renaming a usage node re-points its reference — Reverse re-emits
    /// the identifier under the new name. Unnamed/blank edits are ignored (the node
    /// keeps its previous reference).
    /// </summary>
    private void UpdateUsageNodeName(BlueprintNodeVMV6 node, string? newName)
    {
        if (_workingBlueprint == null || string.IsNullOrWhiteSpace(newName)) return;
        var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == node.BlueprintNodeId);
        if (contractNode is VariableNode vn)
            vn.VarName = newName.Trim();
        NotifyBlueprintEdited();
    }

    /// <summary>
    /// Writes an edited usage-ConstNode's literal value back to the Contract
    /// (2026-08-03). Reverse's NodeToKsNode reads ConstValue ?? ConstName, so the
    /// literal round-trips into the KS text.
    /// </summary>
    private void UpdateUsageConstValue(BlueprintNodeVMV6 node, string? value)
    {
        if (_workingBlueprint == null) return;
        var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == node.BlueprintNodeId);
        if (contractNode is ConstNode cn)
            cn.ConstValue = string.IsNullOrWhiteSpace(value) ? null : value;
        NotifyBlueprintEdited();
    }

    /// <summary>
    /// Writes an edited definition node's declaration name/value back to the Contract (R8).
    /// Renaming a definition node synchronises all same-named usage nodes in the working
    /// blueprint so Reverse produces a consistent declaration + references.
    /// </summary>
    private void UpdateDefinitionNode(BlueprintNodeVMV6 node, string? oldName, string? newName, string? value)
    {
        if (_workingBlueprint == null)
        {
            Log.Warning("[BPEditVM] UpdateDefinitionNode: working blueprint is NULL — edit dropped (id={Id} value={Value})", node.BlueprintNodeId, value);
            return;
        }
        var contractNode = _workingBlueprint.Nodes.FirstOrDefault(n => n.Id == node.BlueprintNodeId);
        if (contractNode == null)
        {
            Log.Warning("[BPEditVM] UpdateDefinitionNode: contract node NOT FOUND (id={Id} value={Value})", node.BlueprintNodeId, value);
            return;
        }
        Log.Information("[BPEditVM] UpdateDefinitionNode: id={Id} name={NewName} value={Value}", node.BlueprintNodeId, newName, value);
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
                    RefreshDefinitionNames();
                }
                vn.VarInitialValue = string.IsNullOrWhiteSpace(value) ? null : value;
                break;
        }
        NotifyBlueprintEdited();
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

