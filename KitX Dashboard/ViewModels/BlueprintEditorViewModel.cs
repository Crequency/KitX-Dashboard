using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NodeEditor.Controls;
using NodeEditor.Mvvm;
using NodeEditor.Model;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Tasks;
using KitX.Core.Tasks;
using Serilog;
using BlueprintPinDirection = KitX.Core.Contract.Workflow.PinDirection;

namespace KitX.Dashboard.ViewModels;

public partial class BlueprintEditorViewModel : ObservableObject
{
    private readonly IBlueprintService _blueprintService;
    private readonly IWorkflowService _workflowService;
    private readonly ITasksService _tasksService;
    private readonly INodeTemplateProvider _nodeTemplateProvider;
    private readonly IKcsFileService _kcsFileService;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Last exported BlockScript source code (for display to user)
    /// </summary>
    public string? LastExportedSourceCode { get; private set; }

    /// <summary>
    /// Current file path (null if never saved)
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    /// <summary>
    /// Gets the NodeEditor Editor ViewModel
    /// </summary>
    public EditorViewModel Editor { get; }

    /// <summary>
    /// Gets the Drawing (canvas) ViewModel
    /// </summary>
    public DrawingNodeViewModel Drawing => (DrawingNodeViewModel)Editor.Drawing!;

    private Blueprint? _currentBlueprint;
    private string _statusText = "Ready";
    private bool _isExecuting;

    /// <summary>
    /// Maps "nodeId:pinName" to original BlueprintPin.Id for round-trip export
    /// </summary>
    private Dictionary<string, string> _originalPinIds = new();

    /// <summary>
    /// Maps NodeViewModel to original BlueprintNode.Id for round-trip export
    /// </summary>
    private Dictionary<NodeViewModel, string> _nodeIdMap = new();

    /// <summary>
    /// Gets or sets the current blueprint
    /// </summary>
    public Blueprint? CurrentBlueprint
    {
        get => _currentBlueprint;
        set => SetProperty(ref _currentBlueprint, value);
    }

    /// <summary>
    /// Gets or sets the status text
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// Gets or sets whether execution is in progress
    /// </summary>
    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetProperty(ref _isExecuting, value);
    }

    /// <summary>
    /// Constructor with DI injection
    /// </summary>
    public BlueprintEditorViewModel(
        IBlueprintService blueprintService,
        IWorkflowService workflowService,
        ITasksService tasksService,
        INodeTemplateProvider nodeTemplateProvider,
        IKcsFileService kcsFileService)
    {
        _blueprintService = blueprintService;
        _workflowService = workflowService;
        _tasksService = tasksService;
        _nodeTemplateProvider = nodeTemplateProvider;
        _kcsFileService = kcsFileService;

        // Initialize Editor with Drawing
        var drawing = CreateDrawing();
        Editor = new EditorViewModel
        {
            Drawing = drawing,
            Factory = new BlueprintNodeFactory()
        };

        Log.Information("BlueprintEditorViewModel initialized");
    }

    /// <summary>
    /// Creates a new drawing canvas
    /// </summary>
    private DrawingNodeViewModel CreateDrawing()
    {
        var settings = new DrawingNodeSettingsViewModel
        {
            EnableMultiplePinConnections = true,
            EnableSnap = true,
            SnapX = 15.0,
            SnapY = 15.0,
            EnableGrid = true,
            GridCellWidth = 15.0,
            GridCellHeight = 15.0
        };

        var drawing = new DrawingNodeViewModel
        {
            Settings = settings,
            X = 0,
            Y = 0,
            Width = 1200,
            Height = 800,
            Nodes = new ObservableCollection<INode>(),
            Connectors = new ObservableCollection<IConnector>()
        };

        return drawing;
    }

    /// <summary>
    /// Loads a Blueprint into the drawing canvas
    /// </summary>
    /// <param name="blueprint">The blueprint to load</param>
    public void LoadBlueprintIntoDrawing(Blueprint blueprint)
    {
        if (blueprint == null) return;

        // Clear existing nodes and connectors
        Drawing.Nodes.Clear();
        Drawing.Connectors.Clear();

        // Dictionary to track node mappings (BlueprintNode.Id -> NodeViewModel)
        var nodeMap = new Dictionary<string, NodeViewModel>();

        // Dictionary to track pin mappings (BlueprintPin.Id -> IPin)
        // Clear and rebuild pin ID mapping
        _originalPinIds.Clear();

        var pinMap = new Dictionary<string, IPin>();

        Log.Information("Loading blueprint: {NodeCount} nodes, {ConnectionCount} connections",
            blueprint.Nodes.Count, blueprint.Connections.Count);

        // Convert and add nodes, also populate pinMap
        foreach (var blueprintNode in blueprint.Nodes)
        {
            var nodeVm = ConvertBlueprintNodeToViewModel(blueprintNode);
            nodeVm.Parent = Drawing;
            Drawing.Nodes.Add(nodeVm);
            nodeMap[blueprintNode.Id] = nodeVm;

            // Map each BlueprintPin.Id to the corresponding IPin and store original IDs
            foreach (var blueprintPin in blueprintNode.InputPins)
            {
                var pinVm = FindPinByName(nodeVm, blueprintPin.Name);
                if (pinVm != null)
                {
                    pinMap[blueprintPin.Id] = pinVm;
                    _originalPinIds[$"{blueprintNode.Id}:{blueprintPin.Name}"] = blueprintPin.Id;
                }
            }
            foreach (var blueprintPin in blueprintNode.OutputPins)
            {
                var pinVm = FindPinByName(nodeVm, blueprintPin.Name);
                if (pinVm != null)
                {
                    pinMap[blueprintPin.Id] = pinVm;
                    _originalPinIds[$"{blueprintNode.Id}:{blueprintPin.Name}"] = blueprintPin.Id;
                }
            }

            Log.Debug("  Added node: Name={Name}, Id={Id}, Type={Type}, InputPins={InCount}, OutputPins={OutCount}",
                blueprintNode.Name, blueprintNode.Id, blueprintNode.NodeType,
                blueprintNode.InputPins.Count, blueprintNode.OutputPins.Count);
        }

        // Convert and add connections using pinMap
        foreach (var connection in blueprint.Connections)
        {
            Log.Debug("  Processing connection: {SourceId}:{SourcePinId} -> {TargetId}:{TargetPinId}",
                connection.SourceNodeId, connection.SourcePinId, connection.TargetNodeId, connection.TargetPinId);

            if (pinMap.TryGetValue(connection.SourcePinId, out var sourcePin) &&
                pinMap.TryGetValue(connection.TargetPinId, out var targetPin))
            {
                var connector = new ConnectorViewModel
                {
                    Start = sourcePin,
                    End = targetPin,
                    Parent = Drawing
                };
                Drawing.Connectors.Add(connector);
                Log.Debug("    Created connector: {SourcePinId} -> {TargetPinId}",
                    connection.SourcePinId, connection.TargetPinId);
            }
            else
            {
                Log.Warning("    Could not find pins: sourcePin={SourceFound}, targetPin={TargetFound}",
                    pinMap.ContainsKey(connection.SourcePinId),
                    pinMap.ContainsKey(connection.TargetPinId));
            }
        }

        Log.Information("Loaded blueprint with {NodeCount} nodes and {ConnectionCount} connections",
            blueprint.Nodes.Count, blueprint.Connections.Count);
    }

    /// <summary>
    /// Exports the current drawing to a Blueprint
    /// </summary>
    /// <returns>A new Blueprint populated from the drawing</returns>
    public Blueprint ExportDrawingToBlueprint()
    {
        var blueprint = _blueprintService.CreateBlueprint();
        blueprint.Name = CurrentBlueprint?.Name ?? "Untitled";

        // Dictionary to track NodeViewModel -> BlueprintNode
        var nodeMap = new Dictionary<NodeViewModel, BlueprintNode>();

        // Convert nodes
        foreach (var node in Drawing.Nodes)
        {
            if (node is NodeViewModel nodeVm)
            {
                var blueprintNode = ConvertViewModelToBlueprintNode(nodeVm);
                blueprint.AddNode(blueprintNode);
                nodeMap[nodeVm] = blueprintNode;
            }
        }

        // Convert connections - use original pin IDs from mapping
        foreach (var connector in Drawing.Connectors)
        {
            if (connector is ConnectorViewModel connectorVm &&
                connectorVm.Start?.Parent is NodeViewModel sourceNode &&
                connectorVm.End?.Parent is NodeViewModel targetNode)
            {
                // Look up original node IDs from map
                _nodeIdMap.TryGetValue(sourceNode, out var sourceNodeId);
                _nodeIdMap.TryGetValue(targetNode, out var targetNodeId);
                var sourcePinKey = $"{sourceNodeId}:{connectorVm.Start.Name}";
                var targetPinKey = $"{targetNodeId}:{connectorVm.End.Name}";

                _originalPinIds.TryGetValue(sourcePinKey, out var sourcePinId);
                _originalPinIds.TryGetValue(targetPinKey, out var targetPinId);

                var connection = new BlueprintConnection
                {
                    SourceNodeId = sourceNodeId ?? sourceNode.Name,
                    SourcePinId = sourcePinId ?? connectorVm.Start.Name,
                    TargetNodeId = targetNodeId ?? targetNode.Name,
                    TargetPinId = targetPinId ?? connectorVm.End.Name
                };
                blueprint.AddConnection(connection);
            }
        }

        Log.Information("Exported drawing with {NodeCount} nodes and {ConnectionCount} connections",
            blueprint.Nodes.Count, blueprint.Connections.Count);

        return blueprint;
    }

    /// <summary>
    /// Converts a BlueprintNode (domain model) to NodeViewModel (UI model)
    /// </summary>
    private NodeViewModel ConvertBlueprintNodeToViewModel(BlueprintNode blueprintNode)
    {
        // Use template to get correct size
        var (templateWidth, templateHeight) = _nodeTemplateProvider.GetNodeSize(blueprintNode.NodeType);

        var nodeVm = new NodeViewModel
        {
            Name = blueprintNode.Name,
            X = blueprintNode.X,
            Y = blueprintNode.Y,
            Width = templateWidth,
            Height = templateHeight,
            Content = new BlueprintNodeContentViewModel { Title = blueprintNode.Name },
            Pins = new ObservableCollection<IPin>()
        };

        // Store the mapping for round-trip export
        _nodeIdMap[nodeVm] = blueprintNode.Id;

        // Add input pins
        foreach (var pin in blueprintNode.InputPins)
        {
            var pinVm = CreatePinViewModel(pin, nodeVm, blueprintNode.NodeType);
            nodeVm.Pins.Add(pinVm);
        }

        // Add output pins
        foreach (var pin in blueprintNode.OutputPins)
        {
            var pinVm = CreatePinViewModel(pin, nodeVm, blueprintNode.NodeType);
            nodeVm.Pins.Add(pinVm);
        }

        // Add type-specific content
        switch (blueprintNode)
        {
            case ConstNode constNode:
                ((BlueprintNodeContentViewModel)nodeVm.Content).Title = $"Const: {constNode.ConstName}";
                break;
            case CallNode callNode:
                ((BlueprintNodeContentViewModel)nodeVm.Content).Title = $"Call: {callNode.FunctionName}";
                break;
            case CallHelperNode helperNode:
                ((BlueprintNodeContentViewModel)nodeVm.Content).Title = $"Helper: {helperNode.HelperFunctionName}";
                break;
        }

        return nodeVm;
    }

    /// <summary>
    /// Creates a PinViewModel from a BlueprintPin
    /// </summary>
    private PinViewModel CreatePinViewModel(BlueprintPin pin, NodeViewModel parentNode, BlueprintNodeType nodeType)
    {
        double x = pin.Direction == BlueprintPinDirection.Input ? 0 : parentNode.Width;

        // Use template to get correct Y position
        double y = pin.Direction == BlueprintPinDirection.Input
            ? _nodeTemplateProvider.GetInputPinY(nodeType, pin.Name)
            : _nodeTemplateProvider.GetOutputPinY(nodeType, pin.Name);

        var pinVm = new PinViewModel
        {
            Name = pin.Name,
            Parent = parentNode,
            X = x,
            Y = y,
            Width = 10,
            Height = 10,
            Alignment = pin.Direction == BlueprintPinDirection.Input
                ? PinAlignment.Left
                : PinAlignment.Right,
            Direction = pin.Direction == BlueprintPinDirection.Input
                ? NodeEditor.Model.PinDirection.Input
                : NodeEditor.Model.PinDirection.Output
        };

        return pinVm;
    }

    /// <summary>
    /// Converts a NodeViewModel (UI model) to BlueprintNode (domain model)
    /// </summary>
    private BlueprintNode ConvertViewModelToBlueprintNode(NodeViewModel nodeVm)
    {
        // Infer node type from name
        var nodeType = nodeVm.Name switch
        {
            "Entry" => BlueprintNodeType.Entry,
            "Branch" => BlueprintNodeType.Branch,
            "Loop" => BlueprintNodeType.Loop,
            "Break" => BlueprintNodeType.Break,
            "Const" => BlueprintNodeType.Const,
            "Call" => BlueprintNodeType.Call,
            "CallHelper" => BlueprintNodeType.CallHelper,
            "Print" => BlueprintNodeType.Print,
            "Pause" => BlueprintNodeType.Pause,
            _ => BlueprintNodeType.Entry
        };

        BlueprintNode blueprintNode = nodeType switch
        {
            BlueprintNodeType.Entry => new EntryNode(),
            BlueprintNodeType.Branch => new BranchNode(),
            BlueprintNodeType.Loop => new LoopNode(),
            BlueprintNodeType.Break => new BreakNode(),
            BlueprintNodeType.Const => new ConstNode(),
            BlueprintNodeType.Call => new CallNode(),
            BlueprintNodeType.CallHelper => new CallHelperNode(),
            BlueprintNodeType.Print => new PrintNode(),
            BlueprintNodeType.Pause => new PauseNode(),
            _ => new EntryNode()
        };

        blueprintNode.Name = nodeVm.Name;
        blueprintNode.X = nodeVm.X;
        blueprintNode.Y = nodeVm.Y;
        blueprintNode.Width = nodeVm.Width;
        blueprintNode.Height = nodeVm.Height;

        // Extract type-specific properties from Content.Title
        if (nodeVm.Content is BlueprintNodeContentViewModel content)
        {
            switch (blueprintNode)
            {
                case ConstNode constNode when content.Title.StartsWith("Const:"):
                    constNode.ConstName = content.Title.Substring("Const:".Length).Trim();
                    break;
                case CallNode callNode when content.Title.StartsWith("Call:"):
                    var callParts = content.Title.Substring("Call:".Length).Trim().Split('.');
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
                case CallHelperNode helperNode when content.Title.StartsWith("Helper:"):
                    helperNode.HelperFunctionName = content.Title.Substring("Helper:".Length).Trim();
                    break;
            }
        }

        // Copy pins - use Name as identifier, cast to PinViewModel to access Direction
        foreach (var pin in nodeVm.Pins)
        {
            if (pin is PinViewModel pinVm)
            {
                var blueprintPin = new BlueprintPin
                {
                    Name = pin.Name,
                    Direction = pinVm.Direction == NodeEditor.Model.PinDirection.Input
                        ? BlueprintPinDirection.Input
                        : BlueprintPinDirection.Output
                };

                if (blueprintPin.Direction == BlueprintPinDirection.Input)
                    blueprintNode.InputPins.Add(blueprintPin);
                else
                    blueprintNode.OutputPins.Add(blueprintPin);
            }
        }

        return blueprintNode;
    }

    /// <summary>
    /// Finds a pin in a node by its name
    /// </summary>
    private IPin? FindPinByName(NodeViewModel node, string pinName)
    {
        foreach (var pin in node.Pins)
        {
            if (pin.Name == pinName) return pin;
        }
        return null;
    }

    /// <summary>
    /// Creates a new blueprint
    /// </summary>
    [RelayCommand]
    private void NewBlueprint()
    {
        CurrentBlueprint = _blueprintService.CreateBlueprint();
        StatusText = "New blueprint created";

        // Clear existing nodes
        Drawing.Nodes.Clear();
        Drawing.Connectors.Clear();

        Log.Information("Created new blueprint");
    }

    #region Node Creation Methods

    /// <summary>
    /// Adds a node to the canvas from a template
    /// </summary>
    private void AddNodeFromTemplate(BlueprintNodeType type, string? contentTitle = null)
    {
        var template = _nodeTemplateProvider.GetTemplates()[type];

        var node = new NodeViewModel
        {
            Name = template.Name,
            X = 100,
            Y = 100,
            Width = template.Width,
            Height = template.Height,
            Content = new BlueprintNodeContentViewModel { Title = contentTitle ?? template.Name },
            Pins = new ObservableCollection<IPin>()
        };

        // Add input pins using template positions
        foreach (var pinTemplate in template.InputPins)
        {
            node.AddPin(0, pinTemplate.RelativeY, 10, 10, PinAlignment.Left, pinTemplate.Name);
        }
        // Add output pins using template positions
        foreach (var pinTemplate in template.OutputPins)
        {
            node.AddPin(template.Width, pinTemplate.RelativeY, 10, 10, PinAlignment.Right, pinTemplate.Name);
        }

        node.Parent = Drawing;
        Drawing.Nodes.Add(node);
        Log.Information("Added {NodeType} node", type);
    }

    /// <summary>
    /// Adds a new Entry node to the canvas
    /// </summary>
    public void AddEntryNode() => AddNodeFromTemplate(BlueprintNodeType.Entry);

    /// <summary>
    /// Adds a new Branch node to the canvas
    /// </summary>
    public void AddBranchNode() => AddNodeFromTemplate(BlueprintNodeType.Branch);

    /// <summary>
    /// Adds a new Loop node to the canvas
    /// </summary>
    public void AddLoopNode() => AddNodeFromTemplate(BlueprintNodeType.Loop);

    /// <summary>
    /// Adds a new Break node to the canvas
    /// </summary>
    public void AddBreakNode() => AddNodeFromTemplate(BlueprintNodeType.Break);

    /// <summary>
    /// Adds a new Const node to the canvas
    /// </summary>
    public void AddConstNode() => AddNodeFromTemplate(BlueprintNodeType.Const, "Const: NewConst");

    /// <summary>
    /// Adds a new Call node to the canvas
    /// </summary>
    public void AddCallNode() => AddNodeFromTemplate(BlueprintNodeType.Call, "Call: Plugin.Function");

    /// <summary>
    /// Adds a new Call Helper node to the canvas
    /// </summary>
    public void AddCallHelperNode() => AddNodeFromTemplate(BlueprintNodeType.CallHelper, "Helper: Func");

    /// <summary>
    /// Adds a new Print node to the canvas
    /// </summary>
    public void AddPrintNode() => AddNodeFromTemplate(BlueprintNodeType.Print);

    /// <summary>
    /// Adds a new Pause node to the canvas
    /// </summary>
    public void AddPauseNode() => AddNodeFromTemplate(BlueprintNodeType.Pause);

    #endregion

    #region Commands

    /// <summary>
    /// Command to execute the current blueprint
    /// </summary>
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

        var tokenSource = new CancellationTokenSource();
        _cancellationTokenSource = tokenSource;

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

    /// <summary>
    /// Command to cancel execution
    /// </summary>
    [RelayCommand]
    private void CancelExecution()
    {
        _cancellationTokenSource?.Cancel();
        StatusText = "Execution cancelled";
        Log.Information("Blueprint execution cancelled");
    }

    /// <summary>
    /// Saves the current blueprint to a KCS file
    /// </summary>
    /// <param name="filePath">File path to save to</param>
    public async Task SaveBlueprintAsync(string filePath)
    {
        if (Drawing.Nodes.Count == 0)
        {
            StatusText = "No nodes to save";
            return;
        }

        StatusText = "Saving...";

        try
        {
            // Export current drawing to blueprint
            var blueprint = ExportDrawingToBlueprint();

            // Generate BlockScript source
            var sourceCode = _blueprintService.ExportToBlockScript(blueprint);

            // Build KCS format with blueprint data
            var kcs = new KcsFileFormat
            {
                UseBlockMode = true,
                BlockScriptSource = sourceCode,
                BlueprintData = blueprint,
                HelperFunctions = blueprint.HelperFunctions ?? new List<HelperFunction>(),
                VariableConstants = new Dictionary<string, object?>()
            };

            // Save to file
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

    /// <summary>
    /// Loads a blueprint from a KCS file
    /// </summary>
    /// <param name="filePath">File path to load from</param>
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

            //优先使用 BlueprintData
            if (kcs.BlueprintData != null)
            {
                blueprint = kcs.BlueprintData;
            }
            // 否则从 BlockScriptSource 反向生成
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

    /// <summary>
    /// Command to import from BlockScript
    /// </summary>
    [RelayCommand]
    private async Task ImportFromBlockScriptAsync(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            StatusText = "Empty source code";
            return;
        }

        StatusText = "Importing...";

        try
        {
            var blueprint = _blueprintService.ImportFromBlockScript(sourceCode, null);

            if (blueprint != null)
            {
                CurrentBlueprint = blueprint;
                // Load blueprint into the drawing canvas
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

    /// <summary>
    /// Command to export to BlockScript
    /// </summary>
    [RelayCommand]
    private void ExportToBlockScript()
    {
        if (Drawing.Nodes.Count == 0)
        {
            StatusText = "No nodes to export";
            return;
        }

        StatusText = "Exporting...";

        try
        {
            // Export current drawing to blueprint first
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

    #endregion
}

/// <summary>
/// Content ViewModel for blueprint nodes
/// </summary>
public partial class BlueprintNodeContentViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;
}

/// <summary>
/// Factory for creating blueprint nodes
/// </summary>
public class BlueprintNodeFactory : INodeFactory
{
    public IDrawingNode CreateDrawing(string? name = null)
    {
        var settings = new DrawingNodeSettingsViewModel
        {
            EnableMultiplePinConnections = true,
            EnableSnap = true,
            SnapX = 15.0,
            SnapY = 15.0,
            EnableGrid = true,
            GridCellWidth = 15.0,
            GridCellHeight = 15.0
        };

        return new DrawingNodeViewModel
        {
            Settings = settings,
            Name = name,
            X = 0,
            Y = 0,
            Width = 1200,
            Height = 800,
            Nodes = new ObservableCollection<INode>(),
            Connectors = new ObservableCollection<IConnector>()
        };
    }

    public IList<INodeTemplate> CreateTemplates()
    {
        return new ObservableCollection<INodeTemplate>
        {
            new NodeTemplateViewModel
            {
                Title = "Entry",
                Template = CreateNode("Entry", 0, 0),
                Preview = CreateNode("Entry", 0, 0)
            },
            new NodeTemplateViewModel
            {
                Title = "Branch",
                Template = CreateNode("Branch", 0, 0),
                Preview = CreateNode("Branch", 0, 0)
            },
            new NodeTemplateViewModel
            {
                Title = "Loop",
                Template = CreateNode("Loop", 0, 0),
                Preview = CreateNode("Loop", 0, 0)
            }
        };
    }

    private INode CreateNode(string name, double x, double y)
    {
        return new NodeViewModel
        {
            Name = name,
            X = x,
            Y = y,
            Width = 120,
            Height = 60,
            Content = new BlueprintNodeContentViewModel { Title = name },
            Pins = new ObservableCollection<IPin>()
        };
    }
}
