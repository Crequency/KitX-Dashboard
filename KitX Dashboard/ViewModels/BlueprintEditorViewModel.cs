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
using KitX.Core.Tasks;
using KitX.Dashboard.Services;
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
    private readonly IBlueprintService _blueprintService;
    private readonly IWorkflowService _workflowService;
    private readonly ITasksService _tasksService;
    private readonly INodeRegistry _nodeRegistry;
    private readonly IKcsFileService _kcsFileService;
    private readonly IBlueprintRenderDataService _renderDataService;
    private readonly IFileDialogService _fileDialogService;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>Last exported BlockScript source code (for display to user)</summary>
    public string? LastExportedSourceCode { get; private set; }

    /// <summary>Current file path (null if never saved)</summary>
    public string? CurrentFilePath { get; private set; }

    private Blueprint? _currentBlueprint;
    private string _statusText = "Ready";
    private bool _isExecuting;
    private string _executionResult = string.Empty;

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

    private IWorkflowEditorBridge? _bridge;

    /// <summary>
    /// Sets the bridge to the associated WorkflowEditor.
    /// Called by BlueprintEditorWindow after construction.
    /// </summary>
    public void SetBridge(IWorkflowEditorBridge bridge)
    {
        _bridge = bridge;
    }

    /// <summary>
    /// Constructor with DI injection
    /// </summary>
    public BlueprintEditorViewModel(
        IBlueprintService blueprintService,
        IWorkflowService workflowService,
        ITasksService tasksService,
        INodeRegistry nodeRegistry,
        IKcsFileService kcsFileService,
        IBlueprintRenderDataService renderDataService,
        IFileDialogService fileDialogService)
    {
        _blueprintService = blueprintService;
        _workflowService = workflowService;
        _tasksService = tasksService;
        _nodeRegistry = nodeRegistry;
        _kcsFileService = kcsFileService;
        _renderDataService = renderDataService;
        _fileDialogService = fileDialogService;

        // Initialize PendingConnection so drag-to-connect works
        PendingConnection = new PendingConnectionViewModelBase(this);

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

        var connection = new BlueprintConnectionVM(this, src, tgt);
        Connections.Add(connection);

        src.IsConnected = true;
        tgt.IsConnected = true;

        RefreshCounts();
        Log.Debug("Connection created: {SrcTitle} -> {TgtTitle}", src.Title, tgt.Title);
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
        ResolveAllGetSetPinTypes();

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
            Input = new ObservableCollection<object>(),
            Output = new ObservableCollection<object>()
        };

        // Add input connectors (execution pins first, then data pins)
        foreach (var pin in blueprintNode.InputPins
            .OrderByDescending(p => p.Type == PinType.Execution)
            .ThenBy(p => p.Name))
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

        // Add output connectors
        foreach (var pin in blueprintNode.OutputPins
            .OrderByDescending(p => p.Type == PinType.Execution)
            .ThenBy(p => p.Name))
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
            // Explicitly update output connector PinType (OnConstTypeChanged may not fire
            // if ConstType equals the field's default value "int")
            foreach (var conn in nodeVm.Output.OfType<BlueprintConnectorVM>())
            {
                if (conn.Title == "Value")
                {
                    conn.PinType = BlueprintNodeVM.ConstTypeToPinType(nodeVm.ConstType);
                    break;
                }
            }
        }

        // Special handling: VariableNode → set VarType + VarName on the VM
        if (blueprintNode is VariableNode varNode)
        {
            if (!string.IsNullOrEmpty(varNode.VarType))
                nodeVm.VarType = varNode.VarType;
            if (!string.IsNullOrEmpty(varNode.VarName))
                nodeVm.VarName = varNode.VarName;
            nodeVm.Metadata["VarName"] = varNode.VarName;
            // Wire type propagation callback
            nodeVm.VarTypeChangedCallback = OnNodeTypeChanged;
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
        var blueprint = _blueprintService.CreateBlueprint();
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
                var varName = node.Metadata.TryGetValue("VarName", out var vn) ? vn
                    : node.DisplayTitle.StartsWith("Var:") ? node.DisplayTitle["Var:".Length..].Trim() : node.DisplayTitle;
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
        // Use NodeType from VM directly (more reliable than name-based inference)
        var blueprintNode = _nodeRegistry.Create(nodeVm.NodeType);

        // Preserve original node ID so connections can reference it
        blueprintNode.Id = nodeVm.BlueprintNodeId;

        blueprintNode.Name = !string.IsNullOrEmpty(nodeVm.Name)
            ? nodeVm.Name
            : nodeVm.DisplayTitle;
        blueprintNode.X = nodeVm.Location.X;
        blueprintNode.Y = nodeVm.Location.Y;

        ApplyDisplayTitleToNode(blueprintNode, nodeVm.DisplayTitle, nodeVm);

        // Special handling: VariableNode → preserve VarType from VM
        if (blueprintNode is VariableNode vNode && !string.IsNullOrEmpty(nodeVm.VarType))
        {
            vNode.VarType = nodeVm.VarType;
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

    // ─── Node Type Inference ─────────────────────────────────────────────

    private static BlueprintNodeType InferNodeTypeFromName(string name) => name switch
    {
        "Entry" => BlueprintNodeType.Entry,
        "Branch" => BlueprintNodeType.Branch,
        "Loop" => BlueprintNodeType.Loop,
        "Break" => BlueprintNodeType.Break,
        "Const" or _ when name.StartsWith("Const:") => BlueprintNodeType.Const,
        "Call" or _ when name.StartsWith("Call:") => BlueprintNodeType.Call,
        "CallHelper" or _ when name.StartsWith("Helper:") => BlueprintNodeType.CallHelper,
        "Print" => BlueprintNodeType.Print,
        "Pause" => BlueprintNodeType.Pause,
        "Get" or _ when name.StartsWith("Get:") => BlueprintNodeType.Get,
        "Set" or _ when name.StartsWith("Set:") => BlueprintNodeType.Set,
        _ => BlueprintNodeType.Entry
    };

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
            case GetNode getNode when title.StartsWith("Get:"):
                getNode.VarName = title["Get:".Length..].Trim();
                // Re-resolve pin type when VarName changes
                if (nodeVm != null)
                {
                    var pinType = ResolveVariablePinType(getNode.VarName);
                    UpdateNodeValuePinType(nodeVm, pinType);
                }
                break;
            case SetNode setNode when title.StartsWith("Set:"):
                setNode.VarName = title["Set:".Length..].Trim();
                // Re-resolve pin type when VarName changes
                if (nodeVm != null)
                {
                    var pinType = ResolveVariablePinType(setNode.VarName);
                    UpdateNodeValuePinType(nodeVm, pinType);
                }
                break;
            case VariableNode vNode when title.StartsWith("Var:"):
                vNode.VarName = title["Var:".Length..].Trim();
                if (nodeVm != null)
                    nodeVm.VarName = vNode.VarName;
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

    /// <summary>
    /// Determines the effective PinType for a data connection by tracing the source node type.
    /// </summary>
    private PinType DetermineEffectivePinType(
        BlueprintConnection connection,
        Dictionary<string, PinType> pinTypeMap)
    {
        if (pinTypeMap.TryGetValue(connection.SourcePinId, out var pt) && pt == PinType.Execution)
            return PinType.Execution;

        var blueprint = _currentBlueprint;
        if (blueprint == null) return PinType.Any;

        var sourceNode = blueprint.GetNodeById(connection.SourceNodeId);
        if (sourceNode == null) return PinType.Any;

        if (sourceNode is ConstNode constNode)
            return TypeStringToPinType(constNode.ConstType);

        if (sourceNode is CallNode callNode)
        {
            var helper = blueprint.HelperFunctions
                .FirstOrDefault(h => h.Name == callNode.FunctionName);
            if (helper != null)
                return TypeStringToPinType(helper.ReturnType);
        }

        if (sourceNode is CallHelperNode helperNode2)
        {
            var helper = blueprint.HelperFunctions
                .FirstOrDefault(h => h.Name == helperNode2.HelperFunctionName);
            if (helper != null)
                return TypeStringToPinType(helper.ReturnType);
        }

        if (sourceNode is GetNode getNode)
        {
            var constVal = blueprint.ConstValues
                .FirstOrDefault(cv => cv.Name == getNode.VarName);
            if (constVal != null)
                return TypeStringToPinType(constVal.Type);
        }

        if (!string.IsNullOrEmpty(connection.PubVarName))
        {
            var constVal = blueprint.ConstValues
                .FirstOrDefault(cv => cv.Name == connection.PubVarName);
            if (constVal != null)
                return TypeStringToPinType(constVal.Type);
        }

        return PinType.Any;
    }

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
    /// Propagates a resolved type to all Get/Set node VMs that reference the given variable.
    /// Updates pin types and connection colors.
    /// </summary>
    private void PropagateTypeToGetSetNodes(string varName, PinType pinType)
    {
        if (string.IsNullOrEmpty(varName)) return;

        foreach (var node in Nodes.OfType<BlueprintNodeVM>())
        {
            if (node.NodeType is not (BlueprintNodeType.Get or BlueprintNodeType.Set)) continue;

            // Extract VarName from display title ("Get: myVar" or "Set: myVar")
            var nodeVarName = node.DisplayTitle.Contains(':')
                ? node.DisplayTitle[(node.DisplayTitle.IndexOf(':') + 1)..].Trim()
                : "";

            if (nodeVarName != varName) continue;

            // Find the Value connector and update its PinType
            UpdateNodeValuePinType(node, pinType);
        }
    }

    /// <summary>
    /// Updates the Value pin's PinType on a Get/Set node VM and refreshes connection colors.
    /// </summary>
    private void UpdateNodeValuePinType(BlueprintNodeVM node, PinType pinType)
    {
        // For GetNode: Value is an output connector
        // For SetNode: Value is an input connector
        var connectors = node.NodeType == BlueprintNodeType.Get
            ? node.Output.OfType<BlueprintConnectorVM>()
            : node.Input.OfType<BlueprintConnectorVM>();

        foreach (var conn in connectors)
        {
            if (conn.Title == "Value")
            {
                conn.PinType = pinType;
                // Connection colors will update automatically if BlueprintConnectionVM
                // subscribes to PinType changes (see Issue 2.2)
                break;
            }
        }
    }

    /// <summary>
    /// Callback invoked when a ConstNode or VariableNode changes its type.
    /// Propagates the new type to all dependent Get/Set nodes.
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
                : changedNode.DisplayTitle.StartsWith("Var:") ? changedNode.DisplayTitle["Var:".Length..].Trim() : "";
            pinType = BlueprintNodeVM.ConstTypeToPinType(changedNode.VarType);
        }
        else return;

        PropagateTypeToGetSetNodes(varName, pinType);
    }

    /// <summary>
    /// Performs an initial type resolution pass on all Get/Set nodes.
    /// Called after loading a blueprint.
    /// </summary>
    private void ResolveAllGetSetPinTypes()
    {
        foreach (var node in Nodes.OfType<BlueprintNodeVM>())
        {
            if (node.NodeType is not (BlueprintNodeType.Get or BlueprintNodeType.Set)) continue;

            var varName = node.DisplayTitle.Contains(':')
                ? node.DisplayTitle[(node.DisplayTitle.IndexOf(':') + 1)..].Trim()
                : "";

            if (string.IsNullOrEmpty(varName)) continue;

            var pinType = ResolveVariablePinType(varName);
            UpdateNodeValuePinType(node, pinType);
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
    /// Adds a node to the canvas from a descriptor
    /// </summary>
    private void AddNodeFromTemplate(BlueprintNodeType type, string? contentTitle = null)
    {
        var descriptor = _nodeRegistry.GetDescriptor(type);
        var title = contentTitle ?? descriptor.DisplayName;
        var (primaryColor, lightColor) = BlueprintNodeVM.GetCategoryColors(type);

        var node = new BlueprintNodeVM
        {
            Location = new Avalonia.Point(100, 100),
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

    [RelayCommand]
    public void AddBranchNode()
    {
        AddNodeFromTemplate(BlueprintNodeType.Branch);
        var branchNode = Nodes.OfType<BlueprintNodeVM>().Last();

        // Create True scope block
        CreateScopeBlockForNode(branchNode, "True");

        // Create False scope block
        CreateScopeBlockForNode(branchNode, "False");
    }

    [RelayCommand]
    public void AddLoopNode()
    {
        AddNodeFromTemplate(BlueprintNodeType.Loop);
        var loopNode = Nodes.OfType<BlueprintNodeVM>().Last();

        // Create LoopBody scope block
        CreateScopeBlockForNode(loopNode, "LoopBody");

        // Create LoopEnd scope block
        CreateScopeBlockForNode(loopNode, "LoopEnd");
    }

    [RelayCommand]
    public void AddBreakNode() => AddNodeFromTemplate(BlueprintNodeType.Break);

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
        AddNodeFromTemplate(BlueprintNodeType.Variable, "Var: NewVar");
        var varNode = Nodes.OfType<BlueprintNodeVM>().Last();
        varNode.VarTypeChangedCallback = OnNodeTypeChanged;
    }

    [RelayCommand]
    public void AddCallNode() => AddNodeFromTemplate(BlueprintNodeType.Call, "Call: Plugin.Function");

    [RelayCommand]
    public void AddCallHelperNode() => AddNodeFromTemplate(BlueprintNodeType.CallHelper, "Helper: Func");

    [RelayCommand]
    public void AddPrintNode() => AddNodeFromTemplate(BlueprintNodeType.Print);

    [RelayCommand]
    public void AddPauseNode() => AddNodeFromTemplate(BlueprintNodeType.Pause);

    // ─── Blueprint Commands ──────────────────────────────────────────────

    [RelayCommand]
    private void NewBlueprint()
    {
        CurrentBlueprint = _blueprintService.CreateBlueprint();
        StatusText = "New blueprint created";
        Nodes.Clear();
        Connections.Clear();
        RefreshCounts();
        Log.Information("Created new blueprint");
    }

    [RelayCommand]
    private async Task ExecuteBlueprintAsync()
    {
        if (CurrentBlueprint == null)
        {
            StatusText = "No blueprint to execute";
            return;
        }

        IsExecuting = true;
        StatusText = "Executing...";

        try
        {
            var result = await _blueprintService.ExecuteBlueprintAsync(CurrentBlueprint);

            if (result.IsSuccess)
            {
                var output = result.Output != null && result.Output.Count > 0
                    ? string.Join("\n", result.Output)
                    : string.Empty;
                ExecutionResult = $"Blocks executed: {result.ExecutedBlockCount}\n" +
                                  $"Execution time: {result.ExecutionTimeMs}ms\n" +
                                  (string.IsNullOrEmpty(output) ? "" : $"Output:\n{output}");
                StatusText = $"Executed: {result.ExecutedBlockCount} blocks, {result.ExecutionTimeMs}ms";
                Log.Information("Blueprint executed successfully: {BlockCount} blocks, {Time}ms",
                    result.ExecutedBlockCount, result.ExecutionTimeMs);
            }
            else
            {
                ExecutionResult = $"Error: {result.ErrorMessage}";
                StatusText = $"Execution failed: {result.ErrorMessage}";
                Log.Error("Blueprint execution failed: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ExecutionResult = $"Execution error: {ex.Message}\n{ex.StackTrace}";
            StatusText = $"Execution error: {ex.Message}";
            Log.Error(ex, "Blueprint execution error");
        }
        finally
        {
            IsExecuting = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void CancelExecution()
    {
        _cancellationTokenSource?.Cancel();
        StatusText = "Execution cancelled";
        Log.Information("Blueprint execution cancelled");
    }

    public async Task SaveBlueprintAsync(string filePath)
    {
        if (Nodes.Count == 0)
        {
            StatusText = "No nodes to save";
            return;
        }

        StatusText = "Saving...";

        try
        {
            var blueprint = ExportDrawingToBlueprint();
            var sourceCode = _blueprintService.ExportToBlockScript(blueprint);

            var kcs = new KcsFileFormat
            {
                UseBlockMode = true,
                BlockScriptSource = sourceCode,
                BlueprintData = blueprint,
                HelperFunctions = blueprint.HelperFunctions ?? new List<HelperFunction>(),
                VariableConstants = new Dictionary<string, object?>()
            };

            await _kcsFileService.SaveKcsFileAsync(filePath, kcs);

            CurrentFilePath = filePath;
            CurrentBlueprint = blueprint;
            StatusText = $"Saved: {blueprint.Nodes.Count} nodes to {System.IO.Path.GetFileName(filePath)}";
            Log.Information("Blueprint saved successfully to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            StatusText = $"Save error: {ex.Message}";
            Log.Error(ex, "Blueprint save error");
        }
    }

    public async Task LoadBlueprintAsync(string filePath)
    {
        StatusText = "Loading...";

        try
        {
            var kcs = await _kcsFileService.LoadKcsFileAsync(filePath);

            if (kcs == null)
            {
                StatusText = "Failed to load file";
                return;
            }

            Blueprint? blueprint = null;

            if (kcs.BlueprintData != null)
            {
                blueprint = kcs.BlueprintData;

                // Fallback: if BlueprintData has empty BlockScopes but BlockScriptSource
                // is available, re-import to rebuild BlockScopes with proper ownership
                if ((blueprint.BlockScopes == null || blueprint.BlockScopes.Count == 0)
                    && !string.IsNullOrEmpty(kcs.BlockScriptSource))
                {
                    Log.Information("BlockScopes empty in BlueprintData, re-importing from BlockScriptSource");
                    blueprint = _blueprintService.ImportFromBlockScript(
                        kcs.BlockScriptSource, kcs.HelperFunctions);
                }
            }
            else if (!string.IsNullOrEmpty(kcs.BlockScriptSource))
            {
                blueprint = _blueprintService.ImportFromBlockScript(kcs.BlockScriptSource, kcs.HelperFunctions);
            }

            if (blueprint != null)
            {
                CurrentBlueprint = blueprint;
                CurrentFilePath = filePath;
                LoadBlueprintIntoDrawing(blueprint);
                StatusText = $"Loaded: {blueprint.Nodes.Count} nodes, {blueprint.Connections.Count} connections";
                Log.Information("Blueprint loaded successfully from {FilePath}", filePath);
            }
            else
            {
                StatusText = "No blueprint data found in file";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Load error: {ex.Message}";
            Log.Error(ex, "Blueprint load error");
        }
    }

    [RelayCommand]
    private async Task ImportFromBlockScriptAsync((string SourceCode, List<HelperFunction>? Helpers) args)
    {
        var sourceCode = args.SourceCode;
        var helperFunctions = args.Helpers;

        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            StatusText = "Empty source code";
            return;
        }

        StatusText = "Importing...";

        try
        {
            var blueprint = _blueprintService.ImportFromBlockScript(sourceCode, helperFunctions);

            if (blueprint != null)
            {
                CurrentBlueprint = blueprint;
                LoadBlueprintIntoDrawing(blueprint);
                ExecutionResult = $"Import successful: {blueprint.Nodes.Count} nodes, {blueprint.Connections.Count} connections";
                StatusText = $"Import successful: {blueprint.Nodes.Count} nodes, {blueprint.Connections.Count} connections";
                Log.Information("Blueprint imported successfully with {NodeCount} nodes", blueprint.Nodes.Count);
            }
            else
            {
                ExecutionResult = "Import failed";
                StatusText = "Import failed";
                Log.Warning("Blueprint import returned null");
            }
        }
        catch (Exception ex)
        {
            ExecutionResult = $"Import error: {ex.Message}";
            StatusText = $"Import error: {ex.Message}";
            Log.Error(ex, "Blueprint import error");
        }
    }

    [RelayCommand]
    private void ExportToBlockScript()
    {
        if (Nodes.Count == 0)
        {
            StatusText = "No nodes to export";
            return;
        }

        StatusText = "Exporting...";

        try
        {
            var blueprint = ExportDrawingToBlueprint();
            LastExportedSourceCode = _blueprintService.ExportToBlockScript(blueprint);
            ExecutionResult = $"Export successful: {blueprint.Nodes.Count} nodes\n\n{LastExportedSourceCode}";
            StatusText = $"Export successful: {blueprint.Nodes.Count} nodes";
            Log.Information("Blueprint exported successfully with {NodeCount} nodes", blueprint.Nodes.Count);
        }
        catch (Exception ex)
        {
            ExecutionResult = $"Export error: {ex.Message}";
            StatusText = $"Export error: {ex.Message}";
            Log.Error(ex, "Blueprint export error");
        }
    }

    [RelayCommand]
    private async Task OpenBlueprintAsync()
    {
        var filters = new List<FileDialogFilter>
        {
            new() { Name = "KCS Files", Extensions = ["kcs"] },
            new() { Name = "All Files", Extensions = ["*"] }
        };

        var filePath = await _fileDialogService.ShowOpenDialogAsync("Open Blueprint", filters);
        if (filePath == null) return;

        await LoadBlueprintAsync(filePath);
    }

    [RelayCommand]
    private async Task SaveBlueprintAsAsync()
    {
        var filters = new List<FileDialogFilter>
        {
            new() { Name = "KCS Files", Extensions = ["kcs"] }
        };

        var suggestedName = !string.IsNullOrEmpty(CurrentFilePath)
            ? System.IO.Path.GetFileName(CurrentFilePath)
            : null;

        var filePath = await _fileDialogService.ShowSaveDialogAsync("Save Blueprint", "kcs", filters, suggestedName);
        if (filePath == null) return;

        await SaveBlueprintAsync(filePath);
    }

    [RelayCommand]
    private async Task ImportFromBSAsync()
    {
        // Import directly from WorkflowEditor via bridge
        if (_bridge == null)
        {
            StatusText = "No WorkflowEditor connected";
            return;
        }

        var sourceCode = _bridge.GetCurrentScript();
        var helpers = _bridge.GetHelperFunctions();

        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            ExecutionResult = "No script found in Workflow Editor";
            StatusText = "No script to import";
            return;
        }

        await ImportFromBlockScriptCommand.ExecuteAsync((sourceCode, helpers));
    }

    [RelayCommand]
    private async Task ExportToBSAsync()
    {
        if (_bridge == null)
        {
            StatusText = "No WorkflowEditor connected";
            return;
        }

        // Export and write back to WorkflowEditor
        ExportToBlockScriptCommand.Execute(null);

        if (!string.IsNullOrEmpty(LastExportedSourceCode))
        {
            var blueprint = CurrentBlueprint;
            var helpers = blueprint?.HelperFunctions;

            _bridge.SetScript(LastExportedSourceCode, helpers);
            ExecutionResult = $"Exported to Workflow Editor: {LastExportedSourceCode.Length} chars";
            StatusText = "Exported to Workflow Editor";
        }
    }

    /// <summary>
    /// Run: Export to WorkflowEditor, then trigger execution.
    /// Output appears in both editors' Output panels.
    /// </summary>
    [RelayCommand]
    private async Task RunViaBridgeAsync()
    {
        if (_bridge == null)
        {
            // Fallback to standalone execution
            await ExecuteBlueprintCommand.ExecuteAsync(null);
            return;
        }

        // Step 1: Export BS to WorkflowEditor
        if (Nodes.Count == 0)
        {
            ExecutionResult = "No nodes to run";
            StatusText = "No nodes to run";
            return;
        }

        try
        {
            var blueprint = ExportDrawingToBlueprint();
            LastExportedSourceCode = _blueprintService.ExportToBlockScript(blueprint);
            var helpers = blueprint.HelperFunctions;

            // Step 2: Write back to WorkflowEditor
            _bridge.SetScript(LastExportedSourceCode, helpers);

            // Step 3: Trigger execution in WorkflowEditor
            ExecutionResult = $"Script exported, triggering execution...\n";
            _bridge.TriggerExecution();

            StatusText = "Script exported and executed via Workflow Editor";
            Log.Information("Blueprint run via bridge: exported {NodeCount} nodes", blueprint.Nodes.Count);
        }
        catch (Exception ex)
        {
            ExecutionResult = $"Run error: {ex.Message}";
            StatusText = $"Run error: {ex.Message}";
            Log.Error(ex, "Blueprint run via bridge error");
        }
    }
}
