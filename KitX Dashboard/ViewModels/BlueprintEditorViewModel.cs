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

            Nodes.Remove(node);
        }

        SelectedNodes.Clear();
        RefreshCounts();
        Log.Information("Deleted {Count} nodes", toRemove.Count);
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

        RefreshCounts();
        Log.Information("Loaded blueprint: {NodeCount} nodes, {ConnCount} connections",
            Nodes.Count, Connections.Count);
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

        return blueprint;
    }

    /// <summary>
    /// Converts a BlueprintNodeVM back to a domain BlueprintNode
    /// </summary>
    private BlueprintNode ConvertViewModelToBlueprintNode(BlueprintNodeVM nodeVm)
    {
        var nodeType = InferNodeTypeFromName(nodeVm.Name ?? nodeVm.DisplayTitle);
        var blueprintNode = _nodeRegistry.Create(nodeType);

        blueprintNode.Name = nodeVm.Name ?? nodeVm.DisplayTitle;
        blueprintNode.X = nodeVm.Location.X;
        blueprintNode.Y = nodeVm.Location.Y;

        ApplyDisplayTitleToNode(blueprintNode, nodeVm.DisplayTitle);

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

    private static void ApplyDisplayTitleToNode(BlueprintNode node, string title)
    {
        switch (node)
        {
            case ConstNode constNode when title.StartsWith("Const:"):
                constNode.ConstName = title["Const:".Length..].Trim();
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
                break;
            case SetNode setNode when title.StartsWith("Set:"):
                setNode.VarName = title["Set:".Length..].Trim();
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

    // ─── Node Creation ───────────────────────────────────────────────────

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

        // Add input connectors
        foreach (var pinDesc in descriptor.InputPins)
        {
            node.Input.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Input,
                PinType = pinDesc.Type
            });
        }

        // Add output connectors
        foreach (var pinDesc in descriptor.OutputPins)
        {
            node.Output.Add(new BlueprintConnectorVM
            {
                Title = pinDesc.Name,
                Flow = ConnectorViewModelBase.ConnectorFlow.Output,
                PinType = pinDesc.Type
            });
        }

        Nodes.Add(node);
        RefreshCounts();
        Log.Information("Added {NodeType} node", type);
    }

    private void RefreshCounts()
    {
        NodeCount = Nodes?.Count ?? 0;
        ConnectionCount = Connections?.Count ?? 0;
    }

    [RelayCommand]
    public void AddEntryNode() => AddNodeFromTemplate(BlueprintNodeType.Entry);

    [RelayCommand]
    public void AddBranchNode() => AddNodeFromTemplate(BlueprintNodeType.Branch);

    [RelayCommand]
    public void AddLoopNode() => AddNodeFromTemplate(BlueprintNodeType.Loop);

    [RelayCommand]
    public void AddBreakNode() => AddNodeFromTemplate(BlueprintNodeType.Break);

    [RelayCommand]
    public void AddConstNode() => AddNodeFromTemplate(BlueprintNodeType.Const, "Const: NewConst");

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
                StatusText = $"Executed: {result.ExecutedBlockCount} blocks, {result.ExecutionTimeMs}ms";
                Log.Information("Blueprint executed successfully: {BlockCount} blocks, {Time}ms",
                    result.ExecutedBlockCount, result.ExecutionTimeMs);
            }
            else
            {
                StatusText = $"Execution failed: {result.ErrorMessage}";
                Log.Error("Blueprint execution failed: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
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
                StatusText = $"Import successful: {blueprint.Nodes.Count} nodes, {blueprint.Connections.Count} connections";
                Log.Information("Blueprint imported successfully with {NodeCount} nodes", blueprint.Nodes.Count);
            }
            else
            {
                StatusText = "Import failed";
                Log.Warning("Blueprint import returned null");
            }
        }
        catch (Exception ex)
        {
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
            StatusText = $"Export successful: {blueprint.Nodes.Count} nodes";
            Log.Information("Blueprint exported successfully with {NodeCount} nodes", blueprint.Nodes.Count);
        }
        catch (Exception ex)
        {
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
        var sourceCode = await _fileDialogService.ShowTextInputDialogAsync(
            "Import from BlockScript",
            "Paste BlockScript source code:",
            string.Empty);

        if (string.IsNullOrWhiteSpace(sourceCode)) return;

        await ImportFromBlockScriptCommand.ExecuteAsync((sourceCode, (List<HelperFunction>?)null));
    }

    [RelayCommand]
    private async Task ExportToBSAsync()
    {
        ExportToBlockScriptCommand.Execute(null);

        if (!string.IsNullOrEmpty(LastExportedSourceCode))
        {
            await _fileDialogService.ShowTextOutputDialogAsync("Exported BlockScript", LastExportedSourceCode);
        }
    }
}
