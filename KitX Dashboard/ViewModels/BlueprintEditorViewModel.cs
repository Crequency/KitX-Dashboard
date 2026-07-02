using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Tasks;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Tasks;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Plugin;
using KitX.Workflow.Abstractions;
using NodifyM.Avalonia.ViewModelBase;
using Serilog;
using BlueprintPinDirection = KitX.Core.Contract.Workflow.PinDirection;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Main ViewModel for the Blueprint Editor, inheriting from NodifyM's editor base.
/// Replaces the old EditorViewModel + DrawingNodeViewModel wrapper approach.
/// </summary>
public partial class BlueprintEditorViewModel : NodifyEditorViewModelBase
{
    // v5.2: IBlueprintService removed; replace with IWorkflowSession + IBsSyncService + IBpEditApplier + ICfgBpRenderer
    // private readonly IBlueprintService _blueprintService;
    private readonly ITasksService _tasksService;
    private readonly INodeRegistry _nodeRegistry;
    private readonly IBlueprintRenderDataService _renderDataService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IBlockScriptExecutor _executor;
    private CancellationTokenSource? _cancellationTokenSource;

    private IBlueprintDebugController? _debugController;
    private Dictionary<string, string> _statementToNodeId = new();
    private Dictionary<string, BlueprintConnectorVM> _variableNameToConnector = new();

    private Blueprint? _currentBlueprint;
    private string _statusText = ViewModelBase.TranslateTextWithSuffix("WorkflowEditor", "Ready") ?? "Ready";
    private bool _isExecuting;
    private string _executionResult = string.Empty;
    private bool _isDebugging;
    private bool _isPaused;
    private double _executionSpeed = 1.0;

    public Blueprint? CurrentBlueprint
    {
        get => _currentBlueprint;
        set => SetProperty(ref _currentBlueprint, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetProperty(ref _isExecuting, value);
    }

    /// <summary>
    /// Execution output displayed in the Output panel
    /// </summary>
    public string ExecutionResult
    {
        get => _executionResult;
        set => SetProperty(ref _executionResult, value);
    }

    public bool IsDebugging
    {
        get => _isDebugging;
        set => SetProperty(ref _isDebugging, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        set => SetProperty(ref _isPaused, value);
    }

    public double ExecutionSpeed
    {
        get => _executionSpeed;
        set
        {
            if (SetProperty(ref _executionSpeed, value) && _debugController != null)
                _debugController.SetSpeed(value >= 1.0 ? KitX.Core.Contract.Workflow.ExecutionSpeed.RealTime : KitX.Core.Contract.Workflow.ExecutionSpeed.Slow);
        }
    }

    private int _nodeCount;
    public int NodeCount
    {
        get => _nodeCount;
        set => SetProperty(ref _nodeCount, value);
    }

    private int _connectionCount;
    public int ConnectionCount
    {
        get => _connectionCount;
        set => SetProperty(ref _connectionCount, value);
    }

    /// <summary>
    /// Scope blocks in this blueprint — each corresponds to a #Block in BlockScript.
    /// Created automatically when adding Branch/Loop nodes.
    /// </summary>
    public ObservableCollection<BlueprintScopeBlockVM> ScopeBlocks { get; } = [];

    /// <summary>
    /// Maps node BlueprintNodeId → scope ScopeId for tracking scope membership.
    /// Nodes not in this map belong to MainBlock.
    /// </summary>
    public Dictionary<string, string> NodeToScopeMap { get; } = [];

    private IPluginService? _pluginService;

    /// <summary>
    /// Plugin functions available for dynamic node creation.
    /// Populated from connected plugins via IPluginService.
    /// </summary>
    public ObservableCollection<PluginFunctionPaletteItem> PluginFunctions { get; } = [];

    /// <summary>
    /// Helper functions available for dynamic node creation.
    /// Populated by WorkflowEditorViewModel from the BS-mode helper list.
    /// </summary>
    public ObservableCollection<HelperFunctionPaletteItem> HelperFunctions { get; } = [];

    /// <summary>Whether any plugin functions are available (controls UI visibility)</summary>
    public bool HasPluginFunctions => PluginFunctions.Count > 0;

    /// <summary>Whether any helper functions are available (controls UI visibility)</summary>
    public bool HasHelperFunctions => HelperFunctions.Count > 0;

    /// <summary>
    /// Plugin triggers available for dynamic node creation.
    /// Populated from connected plugins' SupportedTriggers.
    /// </summary>
    public ObservableCollection<PluginTriggerPaletteItem> PluginTriggers { get; } = [];

    /// <summary>Whether any plugin triggers are available (controls UI visibility)</summary>
    public bool HasPluginTriggers => PluginTriggers.Count > 0;

    /// <summary>
    /// Refreshes the PluginFunctions collection from installed plugins.
    /// Called on init and when plugin status changes.
    /// </summary>
    private void RefreshPluginFunctions()
    {
        PluginFunctions.Clear();

        var installedPlugins = _pluginService?.GetInstalledPlugins();
        if (installedPlugins == null || installedPlugins.Count == 0)
        {
            Log.Debug("[BlueprintPalette] RefreshPluginFunctions: no installed plugins");
            OnPropertyChanged(nameof(HasPluginFunctions));
            return;
        }

        Log.Debug("[BlueprintPalette] RefreshPluginFunctions: {PluginCount} installed plugins", installedPlugins.Count);

        foreach (var plugin in installedPlugins)
        {
            if (plugin.PluginInfo?.Functions == null) continue;

            Log.Debug("[BlueprintPalette] Installed plugin: {Name}, FunctionsCount={FuncCount}",
                plugin.PluginInfo.Name, plugin.PluginInfo.Functions.Count);

            foreach (var func in plugin.PluginInfo.Functions)
            {
                PluginFunctions.Add(new PluginFunctionPaletteItem
                {
                    PluginName = plugin.PluginInfo.Name,
                    FunctionName = func.Name,
                    DisplayName = $"{plugin.PluginInfo.Name}.{func.Name}",
                    Parameters = func.Parameters ?? [],
                    ReturnValueType = func.ReturnValueType ?? "void"
                });
            }
        }

        Log.Debug("[BlueprintPalette] RefreshPluginFunctions: added {Count} plugin functions", PluginFunctions.Count);
        OnPropertyChanged(nameof(HasPluginFunctions));

        // Also refresh trigger list from the same installed plugins
        RefreshPluginTriggers();
    }

    /// <summary>
    /// Refreshes the PluginTriggers collection from installed plugins' SupportedTriggers.
    /// Called alongside RefreshPluginFunctions.
    /// </summary>
    private void RefreshPluginTriggers()
    {
        PluginTriggers.Clear();

        var installedPlugins = _pluginService?.GetInstalledPlugins();
        if (installedPlugins == null || installedPlugins.Count == 0)
        {
            OnPropertyChanged(nameof(HasPluginTriggers));
            return;
        }

        foreach (var plugin in installedPlugins)
        {
            if (plugin.PluginInfo?.SupportedTriggers == null) continue;
            foreach (var trigger in plugin.PluginInfo.SupportedTriggers)
            {
                PluginTriggers.Add(new PluginTriggerPaletteItem
                {
                    PluginName = plugin.PluginInfo.Name,
                    TriggerName = trigger,
                    DisplayName = $"{plugin.PluginInfo.Name}.{trigger}"
                });
            }
        }

        Log.Debug("[BlueprintPalette] RefreshPluginTriggers: added {Count} plugin triggers", PluginTriggers.Count);
        OnPropertyChanged(nameof(HasPluginTriggers));
    }

    private void OnPluginStatusChanged(object? sender, PluginStatusChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshPluginFunctions();
        });
    }

    /// <summary>
    /// Detaches event handlers to prevent memory leaks.
    /// Called by WorkflowEditorWindow.OnClosed.
    /// </summary>
    public void Cleanup()
    {
        if (_pluginService != null)
            _pluginService.PluginStatusChanged -= OnPluginStatusChanged;
    }

    /// <summary>
    /// Constructor with DI injection
    /// </summary>
    public BlueprintEditorViewModel(
        /*IBlueprintService blueprintService,*/ // v5.2: removed
        ITasksService tasksService,
        INodeRegistry nodeRegistry,
        IBlueprintRenderDataService renderDataService,
        IFileDialogService fileDialogService,
        IBlockScriptExecutor executor)
    {
        // _blueprintService = blueprintService; // v5.2: removed
        _tasksService = tasksService;
        _nodeRegistry = nodeRegistry;
        _renderDataService = renderDataService;
        _fileDialogService = fileDialogService;
        _executor = executor;

        // Initialize PendingConnection so drag-to-connect works
        PendingConnection = new PendingConnectionViewModelBase(this);

        // Initialize dynamic node palette from plugin service
        _pluginService = App.GetService<IPluginService>();
        if (_pluginService != null)
            _pluginService.PluginStatusChanged += OnPluginStatusChanged;
        RefreshPluginFunctions();

        Log.Information("BlueprintEditorViewModel initialized (NodifyM)");
    }

    // ─── Connection Creation (NodifyM override) ─────────────────────────

    /// <summary>
    /// Overrides NodifyM's Connect() to validate and create BlueprintConnectionVM
    /// with correct StrokeColorHex. Without this override, connections vanish after drag.
    /// </summary>
    public override void Connect(ConnectorViewModelBase source, ConnectorViewModelBase target)
    {
        if (source is not BlueprintConnectorVM src || target is not BlueprintConnectorVM tgt)
            return;

        if (!ValidateConnection(src, tgt))
            return;

        // Check if already connected
        var alreadyConnected = Connections.OfType<BlueprintConnectionVM>()
            .Any(c => c.Source == src && c.Target == tgt || c.Source == tgt && c.Target == src);
        if (alreadyConnected)
            return;

        // Exec output pins may only have one outgoing connection.
        // Auto-disconnect any existing connection from an Exec output before creating the new one.
        var execOutput = (src.PinType == PinType.Execution && src.Flow == ConnectorViewModelBase.ConnectorFlow.Output) ? src
            : (tgt.PinType == PinType.Execution && tgt.Flow == ConnectorViewModelBase.ConnectorFlow.Output) ? tgt
            : null;
        if (execOutput != null)
        {
            var existing = Connections.OfType<BlueprintConnectionVM>()
                .FirstOrDefault(c => c.Source == execOutput);
            if (existing != null)
            {
                // Update IsConnected on the counterpart connector
                var counterpart = existing.Source == execOutput ? existing.Target as BlueprintConnectorVM
                    : existing.Source as BlueprintConnectorVM;
                if (counterpart != null)
                    counterpart.IsConnected = Connections.OfType<BlueprintConnectionVM>()
                        .Any(c => c != existing && (c.Source == counterpart || c.Target == counterpart));
                Connections.Remove(existing);
                Log.Debug("Auto-disconnected existing Exec output connection from {Title}", execOutput.Title);
            }
        }

        var connection = new BlueprintConnectionVM(this, src, tgt);
        Connections.Add(connection);

        src.IsConnected = true;
        tgt.IsConnected = true;

        // Variadic pin expansion: when a node declares a variadic input/output group and the
        // last pin of that group is connected, auto-append a fresh pin so the user can chain
        // more inputs/outputs (e.g. StringConcat inputs, Switch output arms) without manual adding.
        TryExpandVariadicPins(src, tgt);

        RefreshCounts();
        Log.Debug("Connection created: {SrcTitle} -> {TgtTitle}", src.Title, tgt.Title);
    }

    /// <summary>
    /// Generic variadic-pin expansion. For each connected connector that belongs to a node
    /// declaring a variadic group on its side (input/target or output/source), if the connector
    /// is the last pin of that group, append one fresh pin of the group's type.
    /// Replaces the former StringConcat-name-matched <c>TryExpandStringConcatPins</c>.
    /// </summary>
    private void TryExpandVariadicPins(BlueprintConnectorVM src, BlueprintConnectorVM tgt)
    {
        // Output side grows when an output connector is the drag source; input side grows
        // when an input connector is the drop target.
        TryExpandVariadicSide(src, isOutput: true);
        TryExpandVariadicSide(tgt, isOutput: false);
    }

    private void TryExpandVariadicSide(BlueprintConnectorVM connector, bool isOutput)
    {
        var node = FindParentNode(connector);
        if (node == null) return;

        var spec = GetVariadicSpec(node, isOutput);
        if (spec == null) return;

        // Collect the node's pins that belong to this variadic group (matching PinType).
        var pins = (isOutput ? node.Output : node.Input).OfType<BlueprintConnectorVM>()
            .Where(c => c.PinType == spec.PinType)
            .ToList();
        if (pins.Count == 0) return;
        // Only expand when the LAST pin of the group is the one just connected.
        if (connector != pins[^1]) return;

        // Derive the new pin's index from how many pins of this group the descriptor declares
        // statically (base) vs how many exist now. Next appended = StartIndex + (now - base).
        var baseCount = GetBaseGroupPinCount(node, spec, isOutput);
        var nextIndex = spec.StartIndex + (pins.Count - baseCount);
        var name = string.IsNullOrEmpty(spec.BasePinName)
            ? nextIndex.ToString()
            : $"{spec.BasePinName}{nextIndex}";

        (isOutput ? node.Output : node.Input).Add(new BlueprintConnectorVM
        {
            Title = name,
            Flow = isOutput ? ConnectorViewModelBase.ConnectorFlow.Output : ConnectorViewModelBase.ConnectorFlow.Input,
            PinType = spec.PinType,
            OriginalPinId = Guid.NewGuid().ToString()
        });
    }

    /// <summary>
    /// Looks up the variadic-growth spec declared on a node's descriptor. For builtin-function
    /// nodes the descriptor is rebuilt from the registry by function name (cheap; cached in the
    /// registry). Returns null for non-variadic nodes.
    /// </summary>
    private VariadicPinSpec? GetVariadicSpec(BlueprintNodeVM node, bool isOutput)
    {
        var descriptor = GetBuiltinDescriptor(node);
        return isOutput ? descriptor?.OutputVariadic : descriptor?.InputVariadic;
    }

    /// <summary>
    /// Number of pins of the variadic group's type that the descriptor statically declares
    /// (before any editor-driven expansion). Used to keep appended pin numbering sequential.
    /// </summary>
    private int GetBaseGroupPinCount(BlueprintNodeVM node, VariadicPinSpec spec, bool isOutput)
    {
        var descriptor = GetBuiltinDescriptor(node);
        if (descriptor == null) return 0;
        var basePins = isOutput ? descriptor.OutputPins : descriptor.InputPins;
        return basePins.Count(p => p.Type == spec.PinType);
    }

    private NodeDescriptor? GetBuiltinDescriptor(BlueprintNodeVM node)
    {
        if (string.IsNullOrEmpty(node.BuiltinFunctionName)) return null;
        // Rebuild the descriptor for this builtin function (registry-driven, cheap).
        var tmp = _nodeRegistry.CreateBuiltinFunctionNode(node.BuiltinFunctionName);
        return tmp.GetDescriptor();
    }

    // ─── Connection Disconnection (NodifyM override) ────────────────────

    /// <summary>
    /// Overrides NodifyM's DisconnectConnector to properly update IsConnected
    /// and refresh counts. Triggered by Alt+click on a connector.
    /// </summary>
    public override void DisconnectConnector(ConnectorViewModelBase connector)
    {
        if (connector is not BlueprintConnectorVM bpConn) return;

        var attached = Connections.OfType<BlueprintConnectionVM>()
            .Where(c => c.Source == bpConn || c.Target == bpConn)
            .ToList();

        foreach (var conn in attached)
        {
            // Update IsConnected on counterpart connectors
            if (conn.Source is BlueprintConnectorVM src)
                src.IsConnected = Connections.OfType<BlueprintConnectionVM>()
                    .Any(c => c != conn && (c.Source == src || c.Target == src));
            if (conn.Target is BlueprintConnectorVM tgt)
                tgt.IsConnected = Connections.OfType<BlueprintConnectionVM>()
                    .Any(c => c != conn && (c.Source == tgt || c.Target == tgt));

            Connections.Remove(conn);
        }

        RefreshCounts();
        Log.Debug("Disconnected all connections from connector: {Title}", bpConn.Title);
    }

    // ─── Node Deletion ──────────────────────────────────────────────────

    [RelayCommand]
    private void DeleteSelectedNodes()
    {
        var toRemove = SelectedNodes.ToList();
        if (toRemove.Count == 0) return;

        // Remove connections attached to deleted nodes
        foreach (var node in toRemove.OfType<BlueprintNodeVM>())
        {
            var connectors = node.Input.OfType<BlueprintConnectorVM>()
                .Concat(node.Output.OfType<BlueprintConnectorVM>())
                .ToHashSet();

            var attachedConnections = Connections.OfType<BlueprintConnectionVM>()
                .Where(c => connectors.Contains(c.Source) || connectors.Contains(c.Target))
                .ToList();

            foreach (var conn in attachedConnections)
            {
                // Update IsConnected on the other end
                if (conn.Source is BlueprintConnectorVM src)
                    src.IsConnected = Connections.OfType<BlueprintConnectionVM>()
                        .Any(c => c != conn && (c.Source == src || c.Target == src));
                if (conn.Target is BlueprintConnectorVM tgt)
                    tgt.IsConnected = Connections.OfType<BlueprintConnectionVM>()
                        .Any(c => c != conn && (c.Source == tgt || c.Target == tgt));

                Connections.Remove(conn);
            }

            // Remove node from any scope block's ContainedNodeIds
            foreach (var scope in ScopeBlocks)
            {
                scope.ContainedNodeIds.Remove(node.BlueprintNodeId);
            }

            Nodes.Remove(node);
        }

        // Also remove any selected scope blocks (e.g., if user selects and deletes them)
        foreach (var scope in toRemove.OfType<BlueprintScopeBlockVM>())
        {
            scope.UnsubscribeFromChildNodes();
            ScopeBlocks.Remove(scope);
            Nodes.Remove(scope);
        }

        SelectedNodes.Clear();
        RefreshCounts();
        Log.Information("Deleted {Count} nodes", toRemove.Count);
    }

    // ─── Scope Block Membership ─────────────────────────────────────────

    /// <summary>
    /// Moves all selected BlueprintNodeVMs into the specified scope block.
    /// </summary>
    [RelayCommand]
    private void MoveSelectedNodesToScope(string scopeId)
    {
        var scope = ScopeBlocks.FirstOrDefault(s => s.ScopeId == scopeId);
        if (scope == null) return;

        foreach (var node in SelectedNodes.OfType<BlueprintNodeVM>().ToList())
        {
            // Remove from any existing scope first
            RemoveNodeFromAnyScope(node.BlueprintNodeId);

            // Add to target scope
            if (!scope.ContainedNodeIds.Contains(node.BlueprintNodeId))
            {
                scope.ContainedNodeIds.Add(node.BlueprintNodeId);
                NodeToScopeMap[node.BlueprintNodeId] = scopeId;
            }
        }

        scope.RecalculateBounds();
        Log.Information("Moved {Count} nodes to scope '{ScopeId}'", SelectedNodes.Count, scopeId);
    }

    /// <summary>
    /// Removes all selected BlueprintNodeVMs from their current scope block.
    /// </summary>
    [RelayCommand]
    private void RemoveSelectedNodesFromScope()
    {
        foreach (var node in SelectedNodes.OfType<BlueprintNodeVM>().ToList())
        {
            RemoveNodeFromAnyScope(node.BlueprintNodeId);
        }

        // Recalculate bounds for all affected scopes
        foreach (var scope in ScopeBlocks)
            scope.RecalculateBounds();

        Log.Information("Removed {Count} nodes from their scope blocks", SelectedNodes.Count);
    }

    // ─── Rename Commands ────────────────────────────────────────────────

    /// <summary>
    /// Renames a Const or Variable node via a text input dialog.
    /// </summary>
    [RelayCommand]
    private async Task RenameSelectedNodeAsync(BlueprintNodeVM nodeVm)
    {
        string dialogTitle;
        string dialogPrompt;
        string currentName;

        switch (nodeVm.NodeType)
        {
            case BlueprintNodeType.Const:
                dialogTitle = ViewModelBase.TranslateTextWithSuffix("Blueprint", "RenameConst") ?? "Rename Const";
                dialogPrompt = ViewModelBase.TranslateTextWithSuffix("Blueprint", "RenameConstPrompt") ?? "Enter new constant name:";
                currentName = nodeVm.Metadata.TryGetValue("ConstName", out var cn)
                    ? cn
                    : nodeVm.DisplayTitle.StartsWith("Const:")
                        ? nodeVm.DisplayTitle["Const:".Length..].Trim()
                        : nodeVm.DisplayTitle;
                break;

            case BlueprintNodeType.Variable:
                dialogTitle = ViewModelBase.TranslateTextWithSuffix("Blueprint", "RenameVariable") ?? "Rename Variable";
                dialogPrompt = ViewModelBase.TranslateTextWithSuffix("Blueprint", "RenameVariablePrompt") ?? "Enter new variable name:";
                currentName = nodeVm.Metadata.TryGetValue("VarName", out var vn)
                    ? vn
                    : nodeVm.VarName;
                break;

            default:
                return;
        }

        var newName = await _fileDialogService.ShowTextInputDialogAsync(dialogTitle, dialogPrompt, currentName);
        if (string.IsNullOrWhiteSpace(newName) || newName == currentName)
            return;

        switch (nodeVm.NodeType)
        {
            case BlueprintNodeType.Const:
                nodeVm.DisplayTitle = $"Const: {newName}";
                nodeVm.Metadata["ConstName"] = newName;
                break;

            case BlueprintNodeType.Variable:
                nodeVm.VarName = newName;
                // v5.0: title format is "{VarKind}: {VarName}"
                var kindPrefix = !string.IsNullOrEmpty(nodeVm.VarKind) ? nodeVm.VarKind : "PubVar";
                nodeVm.DisplayTitle = $"{kindPrefix}: {newName}";
                nodeVm.Metadata["VarName"] = newName;
                break;
        }

        Log.Information("Renamed {NodeType} node '{OldName}' to '{NewName}'",
            nodeVm.NodeType, currentName, newName);
    }

    /// <summary>
    /// Propagates a variable rename to all referencing nodes (v5.0: no-op since Get/Set are removed).
    /// Kept as stub for future VariableNode connection-based propagation.
    /// </summary>
    private void PropagateVariableRename(string oldName, string newName)
    {
        // v5.0: Get/Set builtin function nodes no longer exist.
        // Variable rename propagation is now handled via VariableNode connections.
        // Kept as a no-op stub; Phase 5 may add VariableNode-based propagation.
    }

    /// <summary>
    /// Renames a scope block via a text input dialog.
    /// </summary>
    [RelayCommand]
    private async Task RenameScopeBlockAsync(BlueprintScopeBlockVM scopeVm)
    {
        var newName = await _fileDialogService.ShowTextInputDialogAsync(
            ViewModelBase.TranslateTextWithSuffix("Blueprint", "RenameScopeBlock") ?? "Rename Scope Block",
            ViewModelBase.TranslateTextWithSuffix("Blueprint", "RenameScopePrompt") ?? "Enter new scope name:",
            scopeVm.DisplayName);

        if (string.IsNullOrWhiteSpace(newName) || newName == scopeVm.DisplayName)
            return;

        scopeVm.DisplayName = newName;
        Log.Information("Renamed scope block to '{NewName}'", newName);
    }

    // ─── Scope Block Membership ─────────────────────────────────────────

    /// <summary>
    /// Gets the scope ID that a node currently belongs to, or null if none.
    /// </summary>
    public string? GetNodeScopeId(string nodeId)
    {
        return NodeToScopeMap.TryGetValue(nodeId, out var scopeId) ? scopeId : null;
    }

    private void RemoveNodeFromAnyScope(string nodeId)
    {
        if (!NodeToScopeMap.TryGetValue(nodeId, out var currentScopeId))
            return;

        var currentScope = ScopeBlocks.FirstOrDefault(s => s.ScopeId == currentScopeId);
        if (currentScope != null)
        {
            currentScope.ContainedNodeIds.Remove(nodeId);
            currentScope.RecalculateBounds();
        }

        NodeToScopeMap.Remove(nodeId);
    }

    // ─── Connection Validation ───────────────────────────────────────────

    /// <summary>
    /// Validates whether two connectors can be connected.
    /// Rules: opposite flows, execution↔execution only, data type compatibility.
    /// </summary>
    private bool ValidateConnection(BlueprintConnectorVM source, BlueprintConnectorVM target)
    {
        // Rule 1: Must be opposite flows (Output → Input)
        if (source.Flow == target.Flow) return false;

        // Normalize: ensure source is Output, target is Input
        var outputPin = source.Flow == ConnectorViewModelBase.ConnectorFlow.Output ? source : target;
        var inputPin = source.Flow == ConnectorViewModelBase.ConnectorFlow.Input ? source : target;

        var startIsExec = outputPin.PinType == PinType.Execution;
        var endIsExec = inputPin.PinType == PinType.Execution;

        // Rule 2: Execution pins only connect to execution pins
        if (startIsExec != endIsExec) return false;

        // Rule 3: For data pins, check type compatibility
        if (!startIsExec)
        {
            if (outputPin.PinType == PinType.Any || inputPin.PinType == PinType.Any)
                return true;
            if (outputPin.PinType != inputPin.PinType) return false;
        }

        return true;
    }

    // ─── Blueprint Loading ───────────────────────────────────────────────

    /// <summary>
    /// Loads a Blueprint into the editor using three-phase rendering.
    /// Phase 1: Create all nodes with connectors
    /// Phase 2: Create execution flow connections
    /// Phase 3: Create data flow connections
    /// </summary>
    public void LoadBlueprintIntoDrawing(Blueprint blueprint)
    {
        if (blueprint == null) return;

        // Clear existing
        Nodes.Clear();
        Connections.Clear();
        ScopeBlocks.Clear();
        NodeToScopeMap.Clear();

        var connectorMap = new Dictionary<string, BlueprintConnectorVM>();
        var pinTypeMap = new Dictionary<string, PinType>();

        var renderData = _renderDataService.GetRenderData(blueprint);

        Log.Information("Loading blueprint: {NodeCount} nodes, {ExecCount} exec, {DataCount} data connections",
            renderData.AllNodes.Count, renderData.ExecConnections.Count, renderData.DataConnections.Count);

        // === Phase 1: Create all nodes ===
        foreach (var blueprintNode in renderData.AllNodes)
        {
            var nodeVm = ConvertBlueprintNodeToViewModel(blueprintNode);
            Nodes.Add(nodeVm);

            // Map each connector by original pin ID
            foreach (var connector in nodeVm.Input.OfType<BlueprintConnectorVM>())
            {
                if (connector.OriginalPinId != null)
                    connectorMap[connector.OriginalPinId] = connector;
            }
            foreach (var connector in nodeVm.Output.OfType<BlueprintConnectorVM>())
            {
                if (connector.OriginalPinId != null)
                    connectorMap[connector.OriginalPinId] = connector;
            }

            Log.Debug("  Added node: Name={Name}, Id={Id}, Type={Type}",
                blueprintNode.Name, blueprintNode.Id, blueprintNode.NodeType);
        }

        // === Phase 1.5: Create BlockScopes for BlockNodes (v5.0) ===
        InitializeBlockScopes();

        // === Phase 2: Create execution flow connections ===
        foreach (var connection in renderData.ExecConnections)
        {
            CreateConnectionFromBlueprintConnection(connection, connectorMap, blueprint);
        }

        // === Phase 3: Create data flow connections ===
        foreach (var connection in renderData.DataConnections)
        {
            CreateConnectionFromBlueprintConnection(connection, connectorMap, blueprint);
        }

        // Update IsConnected on all connectors
        UpdateAllConnectorStates();

        // === Phase 4: Rebuild ScopeBlocks from BlockScopes ===
        RebuildScopeBlocksFromBlockScopes(blueprint);

        // === Phase 5: Resolve dynamic pin types for Get/Set nodes ===
        ResolveAllVariablePinTypes();

        RefreshCounts();
        Log.Information("Loaded blueprint: {NodeCount} nodes, {ConnCount} connections, {ScopeCount} scope blocks",
            Nodes.Count, Connections.Count, ScopeBlocks.Count);
    }

    /// <summary>
    /// Rebuilds ScopeBlocks from the loaded blueprint's BlockScopes.
    /// Only processes non-MainBlock scopes that have an OwnerNodeId.
    /// </summary>
    private void RebuildScopeBlocksFromBlockScopes(Blueprint blueprint)
    {
        if (blueprint.BlockScopes == null || blueprint.BlockScopes.Count == 0)
        {
            Log.Debug("No BlockScopes to rebuild");
            return;
        }

        // Calculate bounding boxes for each scope to position the NodeGroups
        var nodePositions = new Dictionary<string, BlueprintNodeVM>();
        foreach (var nodeVm in Nodes.OfType<BlueprintNodeVM>())
            nodePositions[nodeVm.BlueprintNodeId] = nodeVm;

        foreach (var scope in blueprint.BlockScopes)
        {
            // Skip MainBlock — it has no visual container
            if (scope.IsMainBlock) continue;
            if (string.IsNullOrEmpty(scope.OwnerNodeId)) continue;

            var scopeId = $"{scope.OwnerArmName}_{scope.OwnerNodeId}";
            var displayName = scope.Name;
            var armName = scope.OwnerArmName ?? string.Empty;
            var headerColor = BlueprintScopeBlockVM.GetHeaderColor(armName);

            // Calculate bounding box from contained nodes
            var bounds = CalculateScopeBounds(scope.NodeIds, nodePositions);

            var scopeBlock = new BlueprintScopeBlockVM
            {
                ScopeId = scopeId,
                DisplayName = displayName,
                ArmName = armName,
                OwnerNodeId = scope.OwnerNodeId,
                Location = bounds.Location,
                GroupSize = bounds.Size,
                HeaderColor = headerColor,
            };

            foreach (var nodeId in scope.NodeIds)
            {
                scopeBlock.ContainedNodeIds.Add(nodeId);
                NodeToScopeMap[nodeId] = scopeId;
            }

            ScopeBlocks.Add(scopeBlock);
            // Also add to Nodes so NodifyEditor renders the NodeGroup
            Nodes.Add(scopeBlock);

            // Set Editor reference for drag propagation and auto-sizing
            scopeBlock.Editor = this;
            scopeBlock.SubscribeToChildNodes();

            Log.Debug("Rebuilt scope block '{Name}' with {Count} nodes, owner={OwnerId}",
                displayName, scope.NodeIds.Count, scope.OwnerNodeId);
        }

        Log.Information("Rebuilt {Count} scope blocks from BlockScopes", ScopeBlocks.Count);
    }

    /// <summary>
    /// Creates BlockNodeScopeVM instances for all BlockNode VMs in the current canvas (v5.0).
    /// Called after Phase 1 of loading so all child nodes are available for lookup.
    /// </summary>
    private void InitializeBlockScopes()
    {
        foreach (var nodeVm in Nodes.OfType<BlueprintNodeVM>())
        {
            if (!nodeVm.IsBlockNode) continue;
            if (nodeVm.BlockScope != null) continue; // already initialized

            var blockScope = new BlockNodeScopeVM
            {
                BlockName = nodeVm.Metadata.TryGetValue("BlockName", out var bn) ? bn : "Block",
                OwnerBlockNodeId = nodeVm.BlueprintNodeId,
                Editor = this,
                Location = nodeVm.Location,
            };

            // Populate child node IDs from metadata
            if (nodeVm.Metadata.TryGetValue("ChildNodeIds", out var childIdsStr) &&
                !string.IsNullOrEmpty(childIdsStr))
            {
                var ids = childIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var id in ids)
                {
                    var trimmed = id.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        blockScope.ContainedNodeIds.Add(trimmed);
                }
            }

            // Check for nesting violation (warn but don't block)
            var nestedBlockId = blockScope.FindNestedBlockNode();
            if (nestedBlockId != null)
            {
                Serilog.Log.Warning("[BlockNode] Block '{BlockName}' contains nested BlockNode '{NestedId}' — " +
                    "nesting is not supported and may cause layout issues",
                    blockScope.BlockName, nestedBlockId);
            }

            // Build preview data
            blockScope.RecalculatePreview();

            nodeVm.BlockScope = blockScope;
            Serilog.Log.Debug("Initialized BlockScope for '{BlockName}' with {Count} child nodes",
                blockScope.BlockName, blockScope.ContainedNodeIds.Count);
        }

        var blockCount = Nodes.OfType<BlueprintNodeVM>().Count(n => n.IsBlockNode);
        if (blockCount > 0)
            Serilog.Log.Information("Initialized {Count} BlockScopes", blockCount);
    }

    /// <summary>
    /// Called when a BlockNode's collapse state is toggled.
    /// Triggers push layout adjustment via LayoutService.
    /// </summary>
    public void OnBlockCollapseToggled(BlockNodeScopeVM blockScope)
    {
        Serilog.Log.Debug("BlockScope '{BlockName}' toggled collapse → {State}",
            blockScope.BlockName, blockScope.IsCollapsed ? "collapsed" : "expanded");

        // Recalculate preview when collapsing
        if (blockScope.IsCollapsed)
        {
            blockScope.RecalculatePreview();
        }

        // Trigger push layout via ILayoutService
        if (CurrentBlueprint != null)
        {
            var layoutService = App.GetService<Workflow.Abstractions.ILayoutService>();
            layoutService?.AdjustLayoutForBlockCollapse(
                CurrentBlueprint,
                blockScope.OwnerBlockNodeId,
                blockScope.IsCollapsed,
                blockScope.ContainedNodeIds.ToList());
        }
    }

    /// <summary>
    /// Calculates the bounding rectangle for a set of nodes,
    /// with padding to create the scope block visual container.
    /// </summary>
    private static (Avalonia.Point Location, Avalonia.Size Size) CalculateScopeBounds(
        List<string> nodeIds,
        Dictionary<string, BlueprintNodeVM> nodePositions)
    {
        if (nodeIds.Count == 0)
            return (new Avalonia.Point(300, 200), new Avalonia.Size(400, 250));

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var nodeId in nodeIds)
        {
            if (nodePositions.TryGetValue(nodeId, out var node))
            {
                minX = Math.Min(minX, node.Location.X);
                minY = Math.Min(minY, node.Location.Y);
                maxX = Math.Max(maxX, node.Location.X + 200); // approximate node width
                maxY = Math.Max(maxY, node.Location.Y + 100); // approximate node height
            }
        }

        if (minX == double.MaxValue)
            return (new Avalonia.Point(300, 200), new Avalonia.Size(400, 250));

        const double padding = 30;
        return (
            new Avalonia.Point(minX - padding, minY - padding - 30), // extra top for header
            new Avalonia.Size(maxX - minX + padding * 2, maxY - minY + padding * 2 + 30)
        );
    }

    /// <summary>
    /// Creates a BlueprintNodeVM from a domain BlueprintNode
    /// </summary>
    private BlueprintNodeVM ConvertBlueprintNodeToViewModel(BlueprintNode blueprintNode)
    {
        var descriptor = blueprintNode.GetDescriptor();
        var displayTitle = blueprintNode.GetDisplayTitle();
        var (primaryColor, lightColor) = BlueprintNodeVM.GetCategoryColors(blueprintNode.NodeType);

        var nodeVm = new BlueprintNodeVM
        {
            Location = new Avalonia.Point(blueprintNode.X, blueprintNode.Y),
            BlueprintNodeId = blueprintNode.Id,
            NodeType = blueprintNode.NodeType,
            Name = blueprintNode.Name,
            DisplayTitle = displayTitle,
            CategoryColor = primaryColor,
            CategoryColorLight = lightColor,
            Title = displayTitle,
            Comment = blueprintNode.Comment,
            Input = new ObservableCollection<object>(),
            Output = new ObservableCollection<object>()
        };

        // Build RelativeY lookup from descriptor for correct pin ordering
        var inputRelativeY = descriptor.InputPins
            .ToDictionary(p => p.Name, p => p.RelativeY);
        var outputRelativeY = descriptor.OutputPins
            .ToDictionary(p => p.Name, p => p.RelativeY);

        // Add input connectors (execution pins first, then data pins, ordered by RelativeY)
        foreach (var pin in blueprintNode.InputPins
            .OrderByDescending(p => p.Type == PinType.Execution)
            .ThenBy(p => inputRelativeY.GetValueOrDefault(p.Name, 0)))
        {
            var connector = new BlueprintConnectorVM
            {
                Title = pin.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                PinType = pin.Type,
                OriginalPinId = pin.Id,
                DefaultValue = pin.DefaultValue
            };
            nodeVm.Input.Add(connector);
        }

        // Add output connectors (ordered by RelativeY to match descriptor layout)
        foreach (var pin in blueprintNode.OutputPins
            .OrderByDescending(p => p.Type == PinType.Execution)
            .ThenBy(p => outputRelativeY.GetValueOrDefault(p.Name, 0)))
        {
            var connector = new BlueprintConnectorVM
            {
                Title = pin.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                PinType = pin.Type,
                OriginalPinId = pin.Id,
                DefaultValue = pin.DefaultValue
            };
            nodeVm.Output.Add(connector);
        }

        // Special handling: ConstNode → set ConstType + ConstValue on the VM
        if (blueprintNode is ConstNode constNode)
        {
            // Initialize ConstType from domain model (triggers OnConstTypeChanged → Metadata + PinType)
            if (!string.IsNullOrEmpty(constNode.ConstType))
                nodeVm.ConstType = constNode.ConstType;
            // Initialize ConstValue (triggers OnConstValueChanged → output connector DefaultValue)
            if (constNode.ConstValue != null)
                nodeVm.ConstValue = constNode.ConstValue;
            // Preserve ConstName in Metadata
            if (!string.IsNullOrEmpty(constNode.ConstName))
                nodeVm.Metadata["ConstName"] = constNode.ConstName;
            // Wire type propagation callback
            nodeVm.ConstTypeChangedCallback = OnNodeTypeChanged;
            // Explicitly update output connector PinType
            foreach (var conn in nodeVm.Output.OfType<BlueprintConnectorVM>())
            {
                if (conn.Title == "Value")
                {
                    conn.PinType = BlueprintNodeVM.ConstTypeToPinType(nodeVm.ConstType);
                    break;
                }
            }
            // Register for debug hover: map const name to output connector
            var constName = !string.IsNullOrEmpty(constNode.ConstName) ? constNode.ConstName : nodeVm.DisplayTitle;
            foreach (var conn in nodeVm.Output.OfType<BlueprintConnectorVM>())
                _variableNameToConnector[constName] = conn;
        }

        // Special handling: VariableNode → set VarKind + VarType + VarName on the VM
        if (blueprintNode is VariableNode varNode)
        {
            // VarKind (v5.0): preserve storage tier
            nodeVm.VarKind = varNode.VarKind.ToString();
            nodeVm.Metadata["VarKind"] = varNode.VarKind.ToString();
            if (!string.IsNullOrEmpty(varNode.VarType))
                nodeVm.VarType = varNode.VarType;
            if (!string.IsNullOrEmpty(varNode.VarName))
                nodeVm.VarName = varNode.VarName;
            nodeVm.Metadata["VarName"] = varNode.VarName;
            // Fix display title to match domain format: "PubVar: name" instead of "Var: name"
            nodeVm.DisplayTitle = varNode.GetDisplayTitle();
            nodeVm.Title = nodeVm.DisplayTitle;
            // Wire type propagation callback
            nodeVm.VarTypeChangedCallback = OnNodeTypeChanged;
            // Register for debug hover: map variable name to output connector
            foreach (var conn in nodeVm.Output.OfType<BlueprintConnectorVM>())
                _variableNameToConnector[varNode.VarName] = conn;
        }

        // Preserve CallNode metadata for round-trip
        if (blueprintNode is CallNode callNode)
        {
            if (!string.IsNullOrEmpty(callNode.PluginName))
                nodeVm.Metadata["PluginName"] = callNode.PluginName;
            if (!string.IsNullOrEmpty(callNode.FunctionName))
                nodeVm.Metadata["FunctionName"] = callNode.FunctionName;
        }

        // Preserve CallHelperNode metadata for round-trip + infer return type
        if (blueprintNode is CallHelperNode helperNode)
        {
            if (!string.IsNullOrEmpty(helperNode.HelperFunctionName))
            {
                nodeVm.Metadata["HelperFunctionName"] = helperNode.HelperFunctionName;

                // Infer return type for the Return output connector
                var helper = _currentBlueprint?.HelperFunctions
                    .FirstOrDefault(h => h.Name == helperNode.HelperFunctionName);
                if (helper != null && !string.IsNullOrEmpty(helper.ReturnType))
                {
                    var returnPinType = TypeStringToPinType(helper.ReturnType);
                    foreach (var conn in nodeVm.Output.OfType<BlueprintConnectorVM>())
                    {
                        if (conn.Title == "Return")
                        {
                            conn.PinType = returnPinType;
                            break;
                        }
                    }
                }
            }
        }

        // Preserve BuiltinFunctionNode metadata for round-trip
        if (blueprintNode is BuiltinFunctionNode bfNode && !string.IsNullOrEmpty(bfNode.FunctionName))
        {
            nodeVm.BuiltinFunctionName = bfNode.FunctionName;
            nodeVm.Metadata["BuiltinFunctionName"] = bfNode.FunctionName;
        }

        // Preserve PluginTriggerNode metadata for round-trip
        if (blueprintNode is PluginTriggerNode ptNode)
        {
            if (!string.IsNullOrEmpty(ptNode.PluginName))
                nodeVm.Metadata["PluginName"] = ptNode.PluginName;
            if (!string.IsNullOrEmpty(ptNode.TriggerName))
                nodeVm.Metadata["TriggerName"] = ptNode.TriggerName;
        }

        // Preserve BlockNode metadata for round-trip (v5.0)
        if (blueprintNode is BlockNode blockNode)
        {
            nodeVm.Metadata["BlockName"] = blockNode.BlockName ?? string.Empty;
            nodeVm.Metadata["ChildNodeIds"] = blockNode.ChildNodeIds != null
                ? string.Join(",", blockNode.ChildNodeIds) : string.Empty;
            nodeVm.Metadata["IsMainBlock"] = blockNode.IsMainBlock.ToString();
            if (!string.IsNullOrEmpty(blockNode.NextBlockName))
                nodeVm.Metadata["NextBlockName"] = blockNode.NextBlockName;
        }

        // Preserve EntryPointNode metadata for round-trip (v5.0)
        if (blueprintNode is EntryPointNode epNode)
        {
            nodeVm.Metadata["PortName"] = epNode.PortName ?? "Value";
        }

        // Preserve ExitPointNode metadata for round-trip (v5.0)
        if (blueprintNode is ExitPointNode xpNode)
        {
            nodeVm.Metadata["PortName"] = xpNode.PortName ?? "Value";
        }

        return nodeVm;
    }

    /// <summary>
    /// Creates a connection from a BlueprintConnection using the connector map
    /// </summary>
    private void CreateConnectionFromBlueprintConnection(
        BlueprintConnection connection,
        Dictionary<string, BlueprintConnectorVM> connectorMap,
        Blueprint blueprint)
    {
        var sourceFound = connectorMap.TryGetValue(connection.SourcePinId, out var sourceConnector);
        var targetFound = connectorMap.TryGetValue(connection.TargetPinId, out var targetConnector);

        if (sourceFound && targetFound && sourceConnector != null && targetConnector != null)
        {
            var connectionVm = new BlueprintConnectionVM(this, sourceConnector, targetConnector);
            Connections.Add(connectionVm);

            // Register PubVar source connector for debug hover
            if (!string.IsNullOrEmpty(connection.PubVarName))
                _variableNameToConnector[connection.PubVarName] = sourceConnector;

            Log.Debug("  Connection: {Source} -> {Target}", connection.SourcePinId, connection.TargetPinId);
        }
        else
        {
            Log.Warning("Could not find connectors for connection: Source={Src} (found={SrcFound}), Target={Tgt} (found={TgtFound})",
                connection.SourcePinId, sourceFound, connection.TargetPinId, targetFound);
        }
    }

    /// <summary>
    /// Updates IsConnected state on all connectors based on current connections
    /// </summary>
    private void UpdateAllConnectorStates()
    {
        var allConnectors = Nodes.OfType<BlueprintNodeVM>()
            .SelectMany(n => n.Input.OfType<BlueprintConnectorVM>()
                .Concat(n.Output.OfType<BlueprintConnectorVM>()));

        foreach (var connector in allConnectors)
        {
            connector.IsConnected = Connections.OfType<BlueprintConnectionVM>()
                .Any(c => c.Source == connector || c.Target == connector);
        }
    }

    // ─── Blueprint Export ────────────────────────────────────────────────

    /// <summary>
    /// Exports the current editor state to a Blueprint domain model
    /// </summary>
    public Blueprint ExportDrawingToBlueprint()
    {
        // v5.2: IBlueprintService removed — ExportDrawingToBlueprint needs migration to new CFG session
        // var blueprint = _blueprintService.CreateBlueprint();
        // ... (migration needed: populate from nodify graph directly)
        throw new NotImplementedException("ExportDrawingToBlueprint: v5.2 migration — use CFG session");
        blueprint.Name = CurrentBlueprint?.Name ?? "Untitled";

        // Preserve HelperFunctions from the original blueprint (imported from BlockScript)
        if (CurrentBlueprint?.HelperFunctions != null && CurrentBlueprint.HelperFunctions.Count > 0)
        {
            blueprint.HelperFunctions = new List<HelperFunction>(CurrentBlueprint.HelperFunctions);
        }

        // Rebuild ConstValues from current ConstNode and VariableNode VMs
        foreach (var node in Nodes.OfType<BlueprintNodeVM>())
        {
            if (node.NodeType == BlueprintNodeType.Const)
            {
                var constName = node.Metadata.TryGetValue("ConstName", out var cn) ? cn
                    : node.DisplayTitle.StartsWith("Const:") ? node.DisplayTitle["Const:".Length..].Trim() : node.DisplayTitle;
                blueprint.ConstValues.Add(new VariableConstant
                {
                    Name = constName,
                    DefaultValue = !string.IsNullOrEmpty(node.ConstValue) ? node.ConstValue : null,
                    Type = node.ConstType ?? "string"
                });
            }
            else if (node.NodeType == BlueprintNodeType.Variable)
            {
                // v5.0: DisplayTitle format is "{VarKind}: {VarName}" (e.g. "PubVar: x")
                var varName = node.Metadata.TryGetValue("VarName", out var vn) ? vn
                    : node.DisplayTitle.Contains(':') ? node.DisplayTitle[(node.DisplayTitle.IndexOf(':') + 1)..].Trim() : node.DisplayTitle;
                blueprint.ConstValues.Add(new VariableConstant
                {
                    Name = varName,
                    DefaultValue = null,
                    Type = node.VarType ?? "int"
                });
            }
        }

        // Convert nodes
        foreach (var node in Nodes)
        {
            if (node is BlueprintNodeVM nodeVm)
            {
                var blueprintNode = ConvertViewModelToBlueprintNode(nodeVm);
                blueprint.AddNode(blueprintNode);
            }
        }

        // Convert connections
        foreach (var connection in Connections)
        {
            if (connection is BlueprintConnectionVM connVm &&
                connVm.Source is BlueprintConnectorVM sourceConn &&
                connVm.Target is BlueprintConnectorVM targetConn)
            {
                // Find parent nodes
                var sourceParent = FindParentNode(sourceConn);
                var targetParent = FindParentNode(targetConn);

                if (sourceParent != null && targetParent != null)
                {
                    var bpConnection = new BlueprintConnection
                    {
                        SourceNodeId = sourceParent.BlueprintNodeId,
                        SourcePinId = sourceConn.OriginalPinId ?? sourceConn.Title,
                        TargetNodeId = targetParent.BlueprintNodeId,
                        TargetPinId = targetConn.OriginalPinId ?? targetConn.Title
                    };
                    blueprint.AddConnection(bpConnection);
                }
            }
        }

        Log.Information("Exported drawing: {NodeCount} nodes, {ConnectionCount} connections",
            blueprint.Nodes.Count, blueprint.Connections.Count);

        // Build BlockScopes from ScopeBlocks
        BuildBlockScopesFromScopeBlocks(blueprint);

        return blueprint;
    }

    /// <summary>
    /// Builds BlockScopes from the current ScopeBlocks state.
    /// MainBlock contains all nodes not assigned to any scope block.
    /// Named blocks contain nodes from their respective ScopeBlocks.
    /// </summary>
    private void BuildBlockScopesFromScopeBlocks(Blueprint blueprint)
    {
        var assignedNodeIds = new HashSet<string>();

        // Build named scope blocks
        foreach (var scope in ScopeBlocks)
        {
            var blockScope = new BlueprintBlockScope
            {
                Name = scope.DisplayName,
                NodeIds = scope.ContainedNodeIds.ToList(),
                OwnerNodeId = scope.OwnerNodeId,
                OwnerArmName = scope.ArmName,
                IsMainBlock = false
            };
            blueprint.BlockScopes.Add(blockScope);

            foreach (var nodeId in scope.ContainedNodeIds)
                assignedNodeIds.Add(nodeId);
        }

        // Build MainBlock scope from unassigned nodes
        var mainBlockNodeIds = new List<string>();
        foreach (var node in Nodes.OfType<BlueprintNodeVM>())
        {
            if (!assignedNodeIds.Contains(node.BlueprintNodeId))
                mainBlockNodeIds.Add(node.BlueprintNodeId);
        }

        if (mainBlockNodeIds.Count > 0 || blueprint.Nodes.Count > 0)
        {
            var mainScope = new BlueprintBlockScope
            {
                Name = "MainBlock",
                NodeIds = mainBlockNodeIds,
                IsMainBlock = true
            };
            // Insert at beginning so MainBlock is first
            blueprint.BlockScopes.Insert(0, mainScope);
        }

        Log.Information("Built {ScopeCount} block scopes from ScopeBlocks ({MainNodes} main, {Assigned} assigned)",
            blueprint.BlockScopes.Count, mainBlockNodeIds.Count, assignedNodeIds.Count);
    }

    /// <summary>
    /// Converts a BlueprintNodeVM back to a domain BlueprintNode
    /// </summary>
    private BlueprintNode ConvertViewModelToBlueprintNode(BlueprintNodeVM nodeVm)
    {
        // Use CreateBuiltinFunctionNode for builtin function nodes to get proper pins
        BlueprintNode blueprintNode;
        if (nodeVm.NodeType == BlueprintNodeType.BuiltinFunction
            && nodeVm.Metadata.TryGetValue("BuiltinFunctionName", out var funcName)
            && !string.IsNullOrEmpty(funcName)
            && funcName is "Print" or "Pause" or "Branch" or "Break" or "StringConcat"
                or "Switch" or "ForLoop" or "Goto"
                or "PluginCall" or "JsonGetField" or "TryGetDevice")
        {
            blueprintNode = _nodeRegistry.CreateBuiltinFunctionNode(funcName);
        }
        else
        {
            blueprintNode = _nodeRegistry.Create(nodeVm.NodeType);
        }

        // Preserve original node ID so connections can reference it
        blueprintNode.Id = nodeVm.BlueprintNodeId;

        blueprintNode.Name = !string.IsNullOrEmpty(nodeVm.Name)
            ? nodeVm.Name
            : nodeVm.DisplayTitle;
        blueprintNode.X = nodeVm.Location.X;
        blueprintNode.Y = nodeVm.Location.Y;

        // Preserve comment for bidirectional comment retention (v5.0)
        blueprintNode.Comment = nodeVm.Comment;

        ApplyDisplayTitleToNode(blueprintNode, nodeVm.DisplayTitle, nodeVm);

        // Special handling: VariableNode → preserve VarKind + VarType from VM
        if (blueprintNode is VariableNode vNode)
        {
            // Restore VarKind (v5.0): from VarKind property or Metadata fallback
            if (!string.IsNullOrEmpty(nodeVm.VarKind)
                && Enum.TryParse<VariableKind>(nodeVm.VarKind, out var parsedKind))
            {
                vNode.VarKind = parsedKind;
            }
            else if (nodeVm.Metadata.TryGetValue("VarKind", out var vkStr)
                && Enum.TryParse<VariableKind>(vkStr, out parsedKind))
            {
                vNode.VarKind = parsedKind;
            }

            if (!string.IsNullOrEmpty(nodeVm.VarType))
                vNode.VarType = nodeVm.VarType;

            // Restore VarName from Metadata (preferred) or VarName property
            if (nodeVm.Metadata.TryGetValue("VarName", out var varName) && !string.IsNullOrEmpty(varName))
                vNode.VarName = varName;
            else if (!string.IsNullOrEmpty(nodeVm.VarName))
                vNode.VarName = nodeVm.VarName;
        }

        // Special handling: PluginTriggerNode → restore PluginName/TriggerName from Metadata
        if (blueprintNode is PluginTriggerNode ptNode)
        {
            if (nodeVm.Metadata.TryGetValue("PluginName", out var pluginName))
                ptNode.PluginName = pluginName ?? string.Empty;
            if (nodeVm.Metadata.TryGetValue("TriggerName", out var triggerName))
                ptNode.TriggerName = triggerName ?? string.Empty;
        }

        // Special handling: BlockNode → restore properties from Metadata (v5.0)
        if (blueprintNode is BlockNode blockNode)
        {
            if (nodeVm.Metadata.TryGetValue("BlockName", out var blockName))
                blockNode.BlockName = blockName;
            if (nodeVm.Metadata.TryGetValue("ChildNodeIds", out var childIds) && !string.IsNullOrEmpty(childIds))
                blockNode.ChildNodeIds = childIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (nodeVm.Metadata.TryGetValue("IsMainBlock", out var isMainStr)
                && bool.TryParse(isMainStr, out var isMain))
                blockNode.IsMainBlock = isMain;
            if (nodeVm.Metadata.TryGetValue("NextBlockName", out var nextBlock))
                blockNode.NextBlockName = nextBlock;
        }

        // Special handling: EntryPointNode → restore PortName from Metadata (v5.0)
        if (blueprintNode is EntryPointNode epNode
            && nodeVm.Metadata.TryGetValue("PortName", out var epPortName))
        {
            epNode.PortName = epPortName;
        }

        // Special handling: ExitPointNode → restore PortName from Metadata (v5.0)
        if (blueprintNode is ExitPointNode xpNode
            && nodeVm.Metadata.TryGetValue("PortName", out var xpPortName))
        {
            xpNode.PortName = xpPortName;
        }

        // Clear auto-generated pins from constructor's InitializePinsFromDescriptor()
        // to prevent duplicates — we add pins from UI connectors instead.
        blueprintNode.InputPins.Clear();
        blueprintNode.OutputPins.Clear();

        // Convert input connectors
        foreach (var input in nodeVm.Input.OfType<BlueprintConnectorVM>())
        {
            var pin = new BlueprintPin
            {
                Name = input.Title ?? string.Empty,
                Type = input.PinType,
                Direction = BlueprintPinDirection.Input,
                DefaultValue = input.DefaultValue
            };
            if (input.OriginalPinId != null)
                pin.Id = input.OriginalPinId;
            blueprintNode.InputPins.Add(pin);
        }

        // Convert output connectors
        foreach (var output in nodeVm.Output.OfType<BlueprintConnectorVM>())
        {
            var pin = new BlueprintPin
            {
                Name = output.Title ?? string.Empty,
                Type = output.PinType,
                Direction = BlueprintPinDirection.Output,
                DefaultValue = output.DefaultValue
            };
            if (output.OriginalPinId != null)
                pin.Id = output.OriginalPinId;
            blueprintNode.OutputPins.Add(pin);
        }

        return blueprintNode;
    }

    /// <summary>
    /// Finds the BlueprintNodeVM that contains a given connector
    /// </summary>
    private BlueprintNodeVM? FindParentNode(BlueprintConnectorVM connector)
    {
        return Nodes.OfType<BlueprintNodeVM>()
            .FirstOrDefault(n => n.Input.Contains(connector) || n.Output.Contains(connector));
    }

    /// <summary>
    /// Finds a BlueprintNodeVM by its BlueprintNodeId.
    /// Used by scope blocks to look up contained child nodes.
    /// </summary>
    public BlueprintNodeVM? FindNodeById(string nodeId)
        => Nodes.OfType<BlueprintNodeVM>().FirstOrDefault(n => n.BlueprintNodeId == nodeId);

    private void ApplyDisplayTitleToNode(BlueprintNode node, string title, BlueprintNodeVM? nodeVm = null)
    {
        switch (node)
        {
            case ConstNode constNode when title.StartsWith("Const:"):
                constNode.ConstName = title["Const:".Length..].Trim();
                if (nodeVm != null)
                {
                    // Restore ConstValue from the VM's ConstValue property
                    if (!string.IsNullOrEmpty(nodeVm.ConstValue))
                        constNode.ConstValue = nodeVm.ConstValue;
                    // Restore ConstType from the VM's ConstType property
                    constNode.ConstType = nodeVm.ConstType;
                }
                break;
            case CallNode callNode when title.StartsWith("Call:"):
                var callParts = title["Call:".Length..].Trim().Split('.');
                if (callParts.Length >= 2)
                {
                    callNode.PluginName = callParts[0];
                    callNode.FunctionName = string.Join(".", callParts.Skip(1));
                }
                else
                {
                    callNode.FunctionName = callParts[0];
                }
                break;
            case CallHelperNode helperNode when title.StartsWith("Helper:"):
                helperNode.HelperFunctionName = title["Helper:".Length..].Trim();
                break;
            case VariableNode vNode when title.StartsWith("Var:") || title.StartsWith("PubVar:") ||
                                         title.StartsWith("Const:") || title.StartsWith("BlockVar:") ||
                                         title.StartsWith("LoopIndex:"):
                // v5.0: title format is "{VarKind}: {VarName}"
                var colonIdx = title.IndexOf(':');
                if (colonIdx > 0)
                {
                    var kindStr = title[..colonIdx];
                    var namePart = title[(colonIdx + 1)..].Trim();
                    vNode.VarName = namePart;
                    if (Enum.TryParse<VariableKind>(kindStr, out var parsedKind))
                        vNode.VarKind = parsedKind;
                }
                if (nodeVm != null)
                {
                    nodeVm.VarName = vNode.VarName;
                    nodeVm.VarKind = vNode.VarKind.ToString();
                }
                break;
            case PluginTriggerNode ptNode when title.StartsWith("Trigger:"):
                var triggerParts = title["Trigger:".Length..].Trim().Split('.');
                if (triggerParts.Length >= 2)
                {
                    ptNode.PluginName = triggerParts[0];
                    ptNode.TriggerName = string.Join(".", triggerParts.Skip(1));
                }
                else
                {
                    ptNode.TriggerName = triggerParts[0];
                }
                break;
            case BlockNode blockNode when title.StartsWith("Block:"):
                blockNode.BlockName = title["Block:".Length..].Trim();
                break;
            case EntryPointNode epNode when title.StartsWith("In:"):
                epNode.PortName = title["In:".Length..].Trim();
                break;
            case ExitPointNode xpNode when title.StartsWith("Out:"):
                xpNode.PortName = title["Out:".Length..].Trim();
                break;
        }
    }

    // ─── Pin Type Helpers ────────────────────────────────────────────────

    private static PinType TypeStringToPinType(string typeStr) => typeStr?.ToLowerInvariant() switch
    {
        "int" or "integer" => PinType.Integer,
        "bool" or "boolean" => PinType.Boolean,
        "double" or "float" or "number" => PinType.Double,
        "string" => PinType.String,
        _ => PinType.Any
    };

    // ─── Dynamic Type Inference & Propagation ────────────────────────────

    /// <summary>
    /// Resolves the PinType for a named variable by searching ConstNodes, VariableNodes,
    /// and CurrentBlueprint.ConstValues.
    /// </summary>
    private PinType ResolveVariablePinType(string varName)
    {
        if (string.IsNullOrEmpty(varName)) return PinType.Any;

        // 1. Search ConstNode VMs in canvas
        foreach (var node in Nodes.OfType<BlueprintNodeVM>())
        {
            if (node.NodeType == BlueprintNodeType.Const)
            {
                var constName = node.Metadata.TryGetValue("ConstName", out var cn) ? cn
                    : node.DisplayTitle.StartsWith("Const:") ? node.DisplayTitle["Const:".Length..].Trim() : "";
                if (constName == varName)
                    return BlueprintNodeVM.ConstTypeToPinType(node.ConstType);
            }
        }

        // 2. Search VariableNode VMs in canvas
        foreach (var node in Nodes.OfType<BlueprintNodeVM>())
        {
            if (node.NodeType == BlueprintNodeType.Variable)
            {
                var vName = node.Metadata.TryGetValue("VarName", out var vn) ? vn
                    : node.DisplayTitle.StartsWith("Var:") ? node.DisplayTitle["Var:".Length..].Trim() : "";
                if (vName == varName)
                    return BlueprintNodeVM.ConstTypeToPinType(node.VarType);
            }
        }

        // 3. Search CurrentBlueprint.ConstValues
        if (CurrentBlueprint?.ConstValues != null)
        {
            var constVal = CurrentBlueprint.ConstValues.FirstOrDefault(cv => cv.Name == varName);
            if (constVal != null)
                return TypeStringToPinType(constVal.Type);
        }

        return PinType.Any;
    }

    /// <summary>
    /// Propagates a resolved type from a VariableNode's Value output pin
    /// to all connected consumer input pins (v5.0: replaces Get/Set propagation).
    /// Updates pin types and connection colors.
    /// </summary>
    private void PropagateTypeToConnectedNodes(BlueprintNodeVM variableNode, PinType pinType)
    {
        if (variableNode.NodeType != BlueprintNodeType.Variable) return;

        // Find the Value output connector on the VariableNode
        var valueOutput = variableNode.Output.OfType<BlueprintConnectorVM>()
            .FirstOrDefault(c => c.Title == "Value");
        if (valueOutput == null) return;

        // Find all connections from this output connector
        foreach (var conn in Connections.OfType<BlueprintConnectionVM>())
        {
            if (conn.Source != valueOutput) continue;

            // Update the connected target connector's PinType
            if (conn.Target is BlueprintConnectorVM targetConn)
            {
                targetConn.PinType = pinType;
            }
        }
    }

    /// <summary>
    /// Updates the Value pin's PinType on any node VM and refreshes connection colors.
    /// Generalized for v5.0 unified VariableNode model (no longer Get/Set-specific).
    /// </summary>
    private void UpdateNodeValuePinType(BlueprintNodeVM node, PinType pinType)
    {
        // Search both Input and Output for a "Value" connector
        foreach (var conn in node.Input.OfType<BlueprintConnectorVM>().Concat(node.Output.OfType<BlueprintConnectorVM>()))
        {
            if (conn.Title == "Value")
            {
                conn.PinType = pinType;
                break;
            }
        }
    }

    /// <summary>
    /// Callback invoked when a ConstNode or VariableNode changes its type.
    /// Propagates the new type to connected consumer nodes (v5.0).
    /// </summary>
    private void OnNodeTypeChanged(BlueprintNodeVM changedNode)
    {
        string varName;
        PinType pinType;

        if (changedNode.NodeType == BlueprintNodeType.Const)
        {
            varName = changedNode.Metadata.TryGetValue("ConstName", out var cn) ? cn
                : changedNode.DisplayTitle.StartsWith("Const:") ? changedNode.DisplayTitle["Const:".Length..].Trim() : "";
            pinType = BlueprintNodeVM.ConstTypeToPinType(changedNode.ConstType);
        }
        else if (changedNode.NodeType == BlueprintNodeType.Variable)
        {
            varName = changedNode.Metadata.TryGetValue("VarName", out var vn) ? vn
                : changedNode.DisplayTitle.Contains(':') ? changedNode.DisplayTitle[(changedNode.DisplayTitle.IndexOf(':') + 1)..].Trim() : "";
            pinType = BlueprintNodeVM.ConstTypeToPinType(changedNode.VarType);

            // v5.0: propagate type through VariableNode's Value output connections
            PropagateTypeToConnectedNodes(changedNode, pinType);
            return;
        }
        else return;

        // For Const nodes, still propagate via name-based lookup (future: can also use connections)
    }

    /// <summary>
    /// Performs an initial type resolution pass on all VariableNodes.
    /// Called after loading a blueprint (v5.0: replaces ResolveAllGetSetPinTypes).
    /// </summary>
    private void ResolveAllVariablePinTypes()
    {
        foreach (var node in Nodes.OfType<BlueprintNodeVM>())
        {
            if (node.NodeType != BlueprintNodeType.Variable) continue;

            var varName = node.Metadata.TryGetValue("VarName", out var vn) ? vn
                : node.DisplayTitle.Contains(':') ? node.DisplayTitle[(node.DisplayTitle.IndexOf(':') + 1)..].Trim() : "";

            if (string.IsNullOrEmpty(varName)) continue;

            var pinType = ResolveVariablePinType(varName);
            // Update the VariableNode's own output pin type
            UpdateNodeValuePinType(node, pinType);
            // Propagate to connected consumer nodes
            PropagateTypeToConnectedNodes(node, pinType);
        }
    }

    // ─── Node Creation ───────────────────────────────────────────────────

    /// <summary>
    /// Creates a scope block for a Branch/Loop node's output arm.
    /// The scope block is positioned to the right of the owner node.
    /// </summary>
    private void CreateScopeBlockForNode(BlueprintNodeVM ownerNode, string armName)
    {
        var scopeId = $"{armName}_{ownerNode.BlueprintNodeId}";
        var displayName = BlueprintScopeBlockVM.GetDefaultDisplayName(armName);
        var headerColor = BlueprintScopeBlockVM.GetHeaderColor(armName);

        // Position scope blocks to the right of the owner node
        var yOffset = armName is "True" or "LoopBody" ? -200 : 200;
        var scopeBlock = new BlueprintScopeBlockVM
        {
            ScopeId = scopeId,
            DisplayName = displayName,
            ArmName = armName,
            OwnerNodeId = ownerNode.BlueprintNodeId,
            Location = new Avalonia.Point(
                ownerNode.Location.X + 300,
                ownerNode.Location.Y + yOffset),
            GroupSize = new Avalonia.Size(400, 250),
            HeaderColor = headerColor,
        };

        ScopeBlocks.Add(scopeBlock);
        // Also add to Nodes collection so NodifyEditor renders it
        Nodes.Add(scopeBlock);

        // Set Editor reference for drag propagation and auto-sizing
        scopeBlock.Editor = this;
        scopeBlock.SubscribeToChildNodes();

        Log.Information("Created scope block '{DisplayName}' for node {NodeId}", displayName, ownerNode.BlueprintNodeId);
    }

    /// <summary>
    /// Computes a spawn location for a newly added node so that consecutive additions
    /// don't all stack on top of each other at (100,100). Cascades in an 8-step ring
    /// around the origin point, then grows outward.
    /// </summary>
    private Avalonia.Point GetNextNodeLocation()
    {
        const double originX = 100;
        const double originY = 100;
        const double step = 30;
        var ring = Nodes.Count % 8;
        var lap = Nodes.Count / 8;
        return new Avalonia.Point(
            originX + (ring + lap) * step,
            originY + ring * step);
    }

    /// <summary>
    /// Adds a node to the canvas from a descriptor
    /// </summary>
    private void AddNodeFromTemplate(BlueprintNodeType type, string? contentTitle = null)
    {
        var descriptor = _nodeRegistry.GetDescriptor(type);
        var title = contentTitle ?? descriptor.DisplayName;
        var (primaryColor, lightColor) = BlueprintNodeVM.GetCategoryColors(type);

        var node = new BlueprintNodeVM
        {
            Location = GetNextNodeLocation(),
            BlueprintNodeId = Guid.NewGuid().ToString(),
            NodeType = type,
            DisplayTitle = title,
            CategoryColor = primaryColor,
            CategoryColorLight = lightColor,
            Title = title,
            Name = descriptor.DisplayName,
            Input = new ObservableCollection<object>(),
            Output = new ObservableCollection<object>()
        };

        // Add input connectors (with stable pin IDs for round-trip export)
        foreach (var pinDesc in descriptor.InputPins)
        {
            var pinId = Guid.NewGuid().ToString();
            node.Input.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                PinType = pinDesc.Type,
                OriginalPinId = pinId
            });
        }

        // Add output connectors (with stable pin IDs for round-trip export)
        foreach (var pinDesc in descriptor.OutputPins)
        {
            var pinId = Guid.NewGuid().ToString();
            node.Output.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                PinType = pinDesc.Type,
                OriginalPinId = pinId
            });
        }

        Nodes.Add(node);
        RefreshCounts();
        Log.Information("Added {NodeType} node", type);
    }

    private void RefreshCounts()
    {
        NodeCount = Nodes?.OfType<BlueprintNodeVM>().Count() ?? 0;
        ConnectionCount = Connections?.Count ?? 0;
    }

    [RelayCommand]
    public void AddEntryNode() => AddNodeFromTemplate(BlueprintNodeType.Entry);

    /// <summary>
    /// Creates a builtin function node from the registry with proper descriptor and UI setup.
    /// </summary>
    private void AddBuiltinFunctionNode(string functionName)
    {
        var builtinNode = _nodeRegistry.CreateBuiltinFunctionNode(functionName);
        var descriptor = builtinNode.GetDescriptor();
        var title = functionName;
        var (primaryColor, lightColor) = BlueprintNodeVM.GetBuiltinFunctionColors(functionName);

        var node = new BlueprintNodeVM
        {
            Location = GetNextNodeLocation(),
            BlueprintNodeId = Guid.NewGuid().ToString(),
            NodeType = BlueprintNodeType.BuiltinFunction,
            BuiltinFunctionName = functionName,
            DisplayTitle = title,
            CategoryColor = primaryColor,
            CategoryColorLight = lightColor,
            Title = title,
            Name = functionName,
            Input = new ObservableCollection<object>(),
            Output = new ObservableCollection<object>()
        };

        foreach (var pinDesc in descriptor.InputPins)
        {
            var pinId = Guid.NewGuid().ToString();
            node.Input.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                PinType = pinDesc.Type,
                OriginalPinId = pinId
            });
        }

        foreach (var pinDesc in descriptor.OutputPins)
        {
            var pinId = Guid.NewGuid().ToString();
            node.Output.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                PinType = pinDesc.Type,
                OriginalPinId = pinId
            });
        }

        Nodes.Add(node);
        RefreshCounts();
        Log.Information("Added BuiltinFunction node: {FunctionName}", functionName);
    }

    [RelayCommand]
    public void AddBranchNode()
    {
        AddBuiltinFunctionNode("Branch");
        var branchNode = Nodes.OfType<BlueprintNodeVM>().Last();
        CreateScopeBlockForNode(branchNode, "True");
        CreateScopeBlockForNode(branchNode, "False");
    }

    [RelayCommand]
    public void AddLoopNode()
    {
        AddBuiltinFunctionNode("Loop");
        var loopNode = Nodes.OfType<BlueprintNodeVM>().Last();
        CreateScopeBlockForNode(loopNode, "LoopBody");
        CreateScopeBlockForNode(loopNode, "LoopEnd");
    }

    [RelayCommand]
    public void AddBreakNode() => AddBuiltinFunctionNode("Break");

    [RelayCommand]
    public void AddConstNode()
    {
        AddNodeFromTemplate(BlueprintNodeType.Const, "Const: NewConst");
        // Initialize output connector PinType from default ConstType
        var constNode = Nodes.OfType<BlueprintNodeVM>().Last(n => n.NodeType == BlueprintNodeType.Const);
        foreach (var conn in constNode.Output.OfType<BlueprintConnectorVM>())
        {
            if (conn.Title == "Value")
            {
                conn.PinType = BlueprintNodeVM.ConstTypeToPinType(constNode.ConstType);
                break;
            }
        }
        constNode.ConstTypeChangedCallback = OnNodeTypeChanged;
    }

    [RelayCommand]
    public void AddVariableNode()
    {
        AddNodeFromTemplate(BlueprintNodeType.Variable, "PubVar: NewVar");
        var varNode = Nodes.OfType<BlueprintNodeVM>().Last();
        varNode.VarKind = "PubVar";
        varNode.VarTypeChangedCallback = OnNodeTypeChanged;
    }

    [RelayCommand]
    public void AddCallNode() => AddNodeFromTemplate(BlueprintNodeType.Call, "Call: Plugin.Function");

    [RelayCommand]
    public void AddCallHelperNode() => AddNodeFromTemplate(BlueprintNodeType.CallHelper, "Helper: Func");

    /// <summary>
    /// Creates a CallNode from a dynamic palette item with correct PluginName/FunctionName
    /// and pre-populated parameter pins.
    /// </summary>
    [RelayCommand]
    private void AddPluginCallNode(PluginFunctionPaletteItem item)
    {
        if (item == null) return;

        var displayTitle = $"Call: {item.PluginName}.{item.FunctionName}";
        var descriptor = _nodeRegistry.GetDescriptor(BlueprintNodeType.Call);
        var (primaryColor, lightColor) = BlueprintNodeVM.GetCategoryColors(BlueprintNodeType.Call);

        var node = new BlueprintNodeVM
        {
            Location = GetNextNodeLocation(),
            BlueprintNodeId = Guid.NewGuid().ToString(),
            NodeType = BlueprintNodeType.Call,
            DisplayTitle = displayTitle,
            CategoryColor = primaryColor,
            CategoryColorLight = lightColor,
            Title = displayTitle,
            Name = descriptor.DisplayName,
            Input = new ObservableCollection<object>(),
            Output = new ObservableCollection<object>()
        };

        node.Metadata["PluginName"] = item.PluginName;
        node.Metadata["FunctionName"] = item.FunctionName;

        // Input: Exec pin + parameter pins
        foreach (var pinDesc in descriptor.InputPins)
            node.Input.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                PinType = pinDesc.Type,
                OriginalPinId = Guid.NewGuid().ToString()
            });

        foreach (var param in item.Parameters)
        {
            node.Input.Add(new BlueprintConnectorVM
            {
                Title = param.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                PinType = TypeStringToPinType(param.Type),
                OriginalPinId = Guid.NewGuid().ToString()
            });
        }

        // Output: Exec pin from descriptor, skip "Return" (we add our own based on actual return type)
        foreach (var pinDesc in descriptor.OutputPins)
        {
            if (pinDesc.Name == "Return") continue;
            node.Output.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                PinType = pinDesc.Type,
                OriginalPinId = Guid.NewGuid().ToString()
            });
        }

        if (!string.IsNullOrEmpty(item.ReturnValueType)
            && !item.ReturnValueType.Equals("void", StringComparison.OrdinalIgnoreCase))
        {
            node.Output.Add(new BlueprintConnectorVM
            {
                Title = "Return",
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                PinType = TypeStringToPinType(item.ReturnValueType),
                OriginalPinId = Guid.NewGuid().ToString()
            });
        }

        Nodes.Add(node);
        RefreshCounts();
        Log.Information("Added CallNode: {Plugin}.{Function} with {ParamCount} params",
            item.PluginName, item.FunctionName, item.Parameters.Count);
    }

    /// <summary>
    /// Creates a PluginTriggerNode from a dynamic palette item.
    /// Trigger nodes are alternative entry points with 0 input pins and 1 Exec output pin.
    /// </summary>
    [RelayCommand]
    private void AddPluginTriggerNode(PluginTriggerPaletteItem item)
    {
        if (item == null) return;

        var displayTitle = $"Trigger: {item.PluginName}.{item.TriggerName}";
        var (primaryColor, lightColor) = BlueprintNodeVM.GetCategoryColors(BlueprintNodeType.Entry);

        var node = new BlueprintNodeVM
        {
            Location = GetNextNodeLocation(),
            BlueprintNodeId = Guid.NewGuid().ToString(),
            NodeType = BlueprintNodeType.PluginTrigger,
            DisplayTitle = displayTitle,
            CategoryColor = primaryColor,
            CategoryColorLight = lightColor,
            Title = displayTitle,
            Name = "PluginTrigger",
            Input = new ObservableCollection<object>(),
            Output = new ObservableCollection<object>()
        };

        node.Metadata["PluginName"] = item.PluginName;
        node.Metadata["TriggerName"] = item.TriggerName;

        // Single Exec output pin (same structure as Entry)
        node.Output.Add(new BlueprintConnectorVM
        {
            Title = "Exec",
            Flow = ConnectorViewModelBase.ConnectorFlow.Output,
            PinType = PinType.Execution,
            OriginalPinId = Guid.NewGuid().ToString()
        });

        Nodes.Add(node);
        RefreshCounts();
        Log.Information("Added PluginTriggerNode: {Plugin}.{Trigger}",
            item.PluginName, item.TriggerName);
    }

    /// <summary>
    /// Creates a CallHelperNode from a dynamic palette item with correct function name
    /// and pre-populated parameter pins.
    /// </summary>
    [RelayCommand]
    private void AddHelperCallNode(HelperFunctionPaletteItem item)
    {
        if (item == null) return;

        var displayTitle = $"Helper: {item.FunctionName}";
        var descriptor = _nodeRegistry.GetDescriptor(BlueprintNodeType.CallHelper);
        var (primaryColor, lightColor) = BlueprintNodeVM.GetCategoryColors(BlueprintNodeType.CallHelper);

        var node = new BlueprintNodeVM
        {
            Location = GetNextNodeLocation(),
            BlueprintNodeId = Guid.NewGuid().ToString(),
            NodeType = BlueprintNodeType.CallHelper,
            DisplayTitle = displayTitle,
            CategoryColor = primaryColor,
            CategoryColorLight = lightColor,
            Title = displayTitle,
            Name = descriptor.DisplayName,
            Input = new ObservableCollection<object>(),
            Output = new ObservableCollection<object>()
        };

        node.Metadata["HelperFunctionName"] = item.FunctionName;

        // Input: Exec pin + parameter pins
        foreach (var pinDesc in descriptor.InputPins)
            node.Input.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                PinType = pinDesc.Type,
                OriginalPinId = Guid.NewGuid().ToString()
            });

        foreach (var param in item.Parameters)
        {
            node.Input.Add(new BlueprintConnectorVM
            {
                Title = param.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                PinType = TypeStringToPinType(param.Type),
                OriginalPinId = Guid.NewGuid().ToString()
            });
        }

        // Output: Exec pin from descriptor, skip "Return" (we add our own based on actual return type)
        foreach (var pinDesc in descriptor.OutputPins)
        {
            if (pinDesc.Name == "Return") continue;
            node.Output.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                PinType = pinDesc.Type,
                OriginalPinId = Guid.NewGuid().ToString()
            });
        }

        if (!string.IsNullOrEmpty(item.ReturnType)
            && !item.ReturnType.Equals("void", StringComparison.OrdinalIgnoreCase)
            && !item.ReturnType.Equals("object", StringComparison.OrdinalIgnoreCase))
        {
            node.Output.Add(new BlueprintConnectorVM
            {
                Title = "Return",
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                PinType = TypeStringToPinType(item.ReturnType),
                OriginalPinId = Guid.NewGuid().ToString()
            });
        }

        Nodes.Add(node);
        RefreshCounts();
        Log.Information("Added CallHelperNode: {Function} with {ParamCount} params",
            item.FunctionName, item.Parameters.Count);
    }

    [RelayCommand]
    public void AddPrintNode() => AddBuiltinFunctionNode("Print");

    [RelayCommand]
    public void AddStringConcatNode() => AddBuiltinFunctionNode("StringConcat");

    [RelayCommand]
    public void AddSwitchNode() => AddBuiltinFunctionNode("Switch");

    [RelayCommand]
    public void AddPauseNode() => AddBuiltinFunctionNode("Pause");

    // ─── Blueprint Commands ──────────────────────────────────────────────

    [RelayCommand]
    private void CancelExecution()
    {
        _cancellationTokenSource?.Cancel();
        StatusText = "Execution cancelled";

        _executor.SetDebugger(null);
        IsDebugging = false;
        IsPaused = false;
        IsExecuting = false;
        CleanupDebugController();
        Log.Information("Blueprint execution cancelled");
    }

    [RelayCommand]
    private async Task RunWithDebugAsync()
    {
        if (CurrentBlueprint == null) return;

        if (IsDebugging)
        {
            CancelExecution();
            return;
        }

        IsDebugging = true;
        IsPaused = true;
        _executionSpeed = 1.0;
        ExecutionResult = string.Empty;

        Log.Information("[BlueprintDebug] Starting debug execution");
        _debugController = new KitX.Workflow.BlockScripting.BlueprintDebugger();
        _debugController.SetSpeed(KitX.Core.Contract.Workflow.ExecutionSpeed.StepByStep);
        _debugController.NodeExecuting += OnDebugNodeExecuting;
        _debugController.NodeExecuted += OnDebugNodeExecuted;
        _debugController.VariableChanged += OnDebugVariableChanged;
        _debugController.ExecutionPaused += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPaused = true;
                Log.Debug("[BlueprintDebug] UI: IsPaused=true");
            });
        _debugController.ExecutionResumed += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPaused = false;
                Log.Debug("[BlueprintDebug] UI: IsPaused=false");
            });

        _executor.SetDebugger(_debugController);

        // v5.2: IBlueprintService removed — debug mapping and execution need migration to ICfgExecutor
        // var mapping = _blueprintService.GetDebugNodeMapping(CurrentBlueprint!);
        // SetDebugNodeMapping(mapping);
        // Log.Information("[BlueprintDebug] Debug node mapping: {Count} entries", mapping.Count);

        IsExecuting = true;
        StatusText = "Debugging...";

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                Log.Information("[BlueprintDebug] Executing with debugger (v5.2: execution path needs migration)");
                // var result = await _blueprintService.ExecuteBlueprintAsync(CurrentBlueprint);

                /* await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (result.IsSuccess)
                    {
                        StatusText = $"Debug done: {result.ExecutedBlockCount} blocks";
                        ExecutionResult = $"Debug complete.\nBlocks: {result.ExecutedBlockCount}\nTime: {result.ExecutionTimeMs}ms";
                    }
                    else
                    {
                        StatusText = $"Debug failed: {result.ErrorMessage}";
                        ExecutionResult = $"Debug error: {result.ErrorMessage}";
                    }
                }); */
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlueprintDebug] Debug execution failed");
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText = $"Debug error: {ex.Message}";
                    ExecutionResult = $"Debug error: {ex.Message}";
                });
            }
            finally
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsExecuting = false;
                    IsDebugging = false;
                    IsPaused = false;
                    // Clear all runtime values from connectors
                    foreach (var conn in _variableNameToConnector.Values)
                        conn.RuntimeValue = null;
                    Log.Information("[BlueprintDebug] Debug execution complete, cleanup");
                });
                _executor.SetDebugger(null);
                CleanupDebugController();
            }
        });
    }

    [RelayCommand]
    private void DebugPause()
    {
        Log.Debug("[BlueprintDebug] UI: Pause clicked");
        _debugController?.Pause();
        StatusText = "Paused";
    }

    [RelayCommand]
    private void DebugStep()
    {
        Log.Debug("[BlueprintDebug] UI: Step clicked");
        _debugController?.StepNext();
        StatusText = "Step";
    }

    [RelayCommand]
    private void DebugContinue()
    {
        Log.Debug("[BlueprintDebug] UI: Continue clicked");
        _debugController?.Continue();
        StatusText = "Debugging...";
    }

    private void OnDebugNodeExecuting(string statementId)
    {
        // Capture output from the PREVIOUS node's execution
        var output = KitX.Workflow.Services.WorkflowOutput.GetAndClear();
        if (output.Length > 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ExecutionResult += output;
            });
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Log.Debug("[BlueprintDebug] NodeExecuting: stmtId={StmtId}", statementId);
            if (_statementToNodeId.TryGetValue(statementId, out var nodeId))
            {
                Log.Debug("[BlueprintDebug] NodeExecuting: mapped to nodeId={NodeId}", nodeId);
                var nodeVm = Nodes.OfType<BlueprintNodeVM>()
                    .FirstOrDefault(n => n.BlueprintNodeId == nodeId);
                if (nodeVm != null)
                {
                    Log.Debug("[BlueprintDebug] NodeExecuting: found nodeVm, setting IsExecuting=true. Title={Title}", nodeVm.Title);
                    nodeVm.IsExecuting = true;
                    Log.Debug("[BlueprintDebug] NodeExecuting: IsExecuting={IsExec}, BorderBrush={Brush}", nodeVm.IsExecuting, nodeVm.BorderBrushOverride);

                    // v5.0 BlockNode: highlight parent BlockNode if this node is inside one
                    foreach (var blockNode in Nodes.OfType<BlueprintNodeVM>())
                    {
                        if (blockNode.IsBlockNode && blockNode.BlockScope != null
                            && blockNode.BlockScope.ContainedNodeIds.Contains(nodeVm.BlueprintNodeId))
                        {
                            blockNode.BlockScope.IsInternalNodeExecuting = true;
                            blockNode.IsExecuting = true; // also highlight the BlockNode itself
                        }
                    }
                }
                else
                {
                    Log.Warning("[BlueprintDebug] NodeExecuting: nodeVm NOT FOUND for BlueprintNodeId={NodeId}. Node count={Count}", nodeId, Nodes.Count);
                }
            }
            else
            {
                Log.Debug("[BlueprintDebug] NodeExecuting: no mapping for stmtId={StmtId}. Mapping count={Count}", statementId, _statementToNodeId.Count);
            }
        });
    }

    private void OnDebugNodeExecuted(string statementId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_statementToNodeId.TryGetValue(statementId, out var nodeId))
            {
                var nodeVm = Nodes.OfType<BlueprintNodeVM>()
                    .FirstOrDefault(n => n.BlueprintNodeId == nodeId);
                if (nodeVm != null)
                {
                    nodeVm.IsExecuting = false;
                    nodeVm.ExecutionCompleted = true;

                    // v5.0 BlockNode: clear parent BlockNode highlight
                    foreach (var blockNode in Nodes.OfType<BlueprintNodeVM>())
                    {
                        if (blockNode.IsBlockNode && blockNode.BlockScope != null
                            && blockNode.BlockScope.ContainedNodeIds.Contains(nodeVm.BlueprintNodeId))
                        {
                            blockNode.BlockScope.IsInternalNodeExecuting = false;
                            blockNode.IsExecuting = false;
                        }
                    }
                }
            }
        });
    }

    private void OnDebugVariableChanged(string name, object? value)
    {
        var valStr = value?.ToString() ?? "null";
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {name} = {valStr}\n";
            ExecutionResult += line;

            // Update connector RuntimeValue for hover display
            if (_variableNameToConnector.TryGetValue(name, out var conn))
                conn.RuntimeValue = valStr;

            // Also propagate to connected input connectors
            foreach (var c in Connections.OfType<BlueprintConnectionVM>())
            {
                if (c.Source == conn)
                {
                    if (c.Target is BlueprintConnectorVM target)
                        target.RuntimeValue = valStr;
                }
            }
        });
    }

    private void CleanupDebugController()
    {
        if (_debugController == null) return;
        _debugController.NodeExecuting -= OnDebugNodeExecuting;
        _debugController.NodeExecuted -= OnDebugNodeExecuted;
        _debugController.VariableChanged -= OnDebugVariableChanged;
        _debugController = null;
    }

    internal void SetDebugNodeMapping(Dictionary<string, string> mapping)
    {
        _statementToNodeId = new Dictionary<string, string>(mapping);
    }
}
