using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Tasks;
using KitX.Core.Contract.Plugin;
using KitX.Core.Event;
using KitX.Core.Tasks;
using KitX.Dashboard;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.Workflow.Backend;
using KitX.Workflow.Ir.Lowering;
using KitX.Workflow.Lens;
using KitX.Workflow.Lens.BsTextLens;
using KitX.Workflow.Lens.BpGraphLens;
using KitX.Workflow.Session;
using Serilog;
using BsTextLens = KitX.Workflow.Lens.BsTextLens.BsTextLens;
using BpGraphLens = KitX.Workflow.Lens.BpGraphLens.BpGraphLens;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Unified workflow editor ViewModel that coordinates BS and BP editing modes.
/// S5: absorbed the former WorkflowScriptEditorWindowViewModel's code-editor,
/// helper-function, constant, and BS-execution responsibilities; holds only
/// the BlueprintVM sub-VM.
/// </summary>
internal partial class WorkflowEditorViewModel : ObservableObject
{
    public enum EditorMode { BlockScript, Blueprint }

    private readonly IWorkflowStorageService _storageService;
    private readonly ITasksService _tasksService;
    private readonly IEventService? _eventService;
    private readonly IPluginServer? _pluginServer;

    // S5: execution backend + cancellation (BS execution now lives in this VM).
    private readonly IExecutionBackend _executionBackend;
    private CancellationTokenSource? _cancellationTokenSource;

    // Phase F1: per-document workflow session (IR as single source of truth).
    // Constructed from BS source on load; drives BP↔BS round-trip via Lens projection.
    // FT.1: Lens deps take the ILens interface (testable without a DI container).
    private WorkflowSession? _session;
    private readonly BsTextLens _bsTextLens;
    private readonly ILens<Blueprint, IReadOnlyList<BpEditAction>> _bpGraphLens;

    private EditorMode _mode = EditorMode.BlockScript;
    private string? _workflowId;
    private string _workflowName = "Untitled Workflow";
    private string _workflowDescription = string.Empty;
    private string _workflowAuthor = string.Empty;
    private bool _isDirty;
    private string _executionOutput = string.Empty;
    private bool _isExecuting;
    private bool _isDebugging;
    private bool _isPaused;

    // ─── Trigger Configuration ──────────────────────────────────────────
    private string _triggerType = "Manual";
    private string? _triggerPluginName;
    private string? _triggerName;

    /// <summary>Available trigger types for ComboBox binding</summary>
    public string[] TriggerTypeOptions { get; } = ["Manual", "PluginEvent"];

    /// <summary>Available plugin names from connected plugins</summary>
    public ObservableCollection<string> AvailablePlugins { get; } = [];

    /// <summary>Available trigger names for the selected plugin</summary>
    public ObservableCollection<string> AvailableTriggers { get; } = [];

    /// <summary>Trigger type: "Manual" (default) or "PluginEvent"</summary>
    public string TriggerType
    {
        get => _triggerType;
        set
        {
            if (SetProperty(ref _triggerType, value))
            {
                OnPropertyChanged(nameof(IsPluginEventTrigger));
                if (value == "PluginEvent")
                    RefreshAvailablePlugins();
                IsDirty = true;
            }
        }
    }

    /// <summary>Whether the trigger type is PluginEvent</summary>
    public bool IsPluginEventTrigger => _triggerType == "PluginEvent";

    /// <summary>Plugin name for PluginEvent triggers</summary>
    public string? TriggerPluginName
    {
        get => _triggerPluginName;
        set
        {
            if (SetProperty(ref _triggerPluginName, value))
            {
                RefreshAvailableTriggers();
                IsDirty = true;
            }
        }
    }

    /// <summary>Trigger name for PluginEvent triggers</summary>
    public string? TriggerName
    {
        get => _triggerName;
        set { if (SetProperty(ref _triggerName, value)) IsDirty = true; }
    }

    /// <summary>
    /// Refreshes the list of available plugins from connected plugin server.
    /// </summary>
    private void RefreshAvailablePlugins()
    {
        AvailablePlugins.Clear();
        if (_pluginServer == null) return;

        foreach (var conn in _pluginServer.Connections)
        {
            if (!string.IsNullOrEmpty(conn.PluginInfo?.Name))
                AvailablePlugins.Add(conn.PluginInfo.Name);
        }
    }

    /// <summary>
    /// Refreshes the list of available triggers for the selected plugin.
    /// </summary>
    private void RefreshAvailableTriggers()
    {
        AvailableTriggers.Clear();
        if (string.IsNullOrEmpty(_triggerPluginName)) return;
        if (_pluginServer == null) return;

        var conn = _pluginServer.Connections
            .FirstOrDefault(c => c.PluginInfo?.Name == _triggerPluginName);
        if (conn?.PluginInfo?.SupportedTriggers == null) return;

        foreach (var trigger in conn.PluginInfo.SupportedTriggers)
            AvailableTriggers.Add(trigger);
    }

    // ─── S5: migrated ScriptVM properties ───────────────────────────────

    /// <summary>The AvaloniaEdit document for the active code editor (main program or helper).</summary>
    [ObservableProperty]
    internal IDocument? _codeDocument;

    /// <summary>The main program BS source text.</summary>
    [ObservableProperty]
    private string? _mainProgramCode;

    /// <summary>The helper function currently selected for editing.</summary>
    [ObservableProperty]
    private HelperFunction? _selectedHelperFunction;

    /// <summary>All helper functions in the workflow.</summary>
    [ObservableProperty]
    private ObservableCollection<HelperFunction> _helperFunctions = [];

    /// <summary>Parameters of the currently selected helper function (UI-bound).</summary>
    [ObservableProperty]
    private ObservableCollection<HelperFunctionParameter> _parameters = [];

    /// <summary>Mutable constants parsed from BS (user can override defaults).</summary>
    [ObservableProperty]
    private ObservableCollection<VariableConstant> _variableConstants = [];

    /// <summary>True when a helper function is selected for editing.</summary>
    public bool IsEditingHelperFunction => SelectedHelperFunction != null;

    /// <summary>
    /// Mirrors the legacy ScriptVM setter side-effects: when the selection changes,
    /// refresh the Parameters list and notify IsEditingHelperFunction.
    /// </summary>
    partial void OnSelectedHelperFunctionChanged(HelperFunction? value)
    {
        OnPropertyChanged(nameof(IsEditingHelperFunction));
        Parameters.Clear();
        if (value?.Parameters != null)
        {
            foreach (var param in value.Parameters)
                Parameters.Add(param);
        }
    }

    /// <summary>
    /// The BP editor sub-ViewModel
    /// </summary>
    public BlueprintEditorViewModel BlueprintVM { get; }

    public EditorMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsBlockScriptMode));
                OnPropertyChanged(nameof(IsBlueprintMode));
            }
        }
    }

    public bool IsBlockScriptMode => _mode == EditorMode.BlockScript;
    public bool IsBlueprintMode => _mode == EditorMode.Blueprint;

    public string WorkflowName
    {
        get => _workflowName;
        set => SetProperty(ref _workflowName, value);
    }

    public string WorkflowDescription
    {
        get => _workflowDescription;
        set => SetProperty(ref _workflowDescription, value);
    }

    public string WorkflowAuthor
    {
        get => _workflowAuthor;
        set => SetProperty(ref _workflowAuthor, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public string ExecutionOutput
    {
        get => _executionOutput;
        set => SetProperty(ref _executionOutput, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetProperty(ref _isExecuting, value);
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

    /// <summary>
    /// Constructor with DI injection. FT.1/FT.2: Lens params take the ILens
    /// interface; event service, plugin server, and Lens services are optional so
    /// the VM can be constructed in a test host without a bootstrapped DI container.
    /// </summary>
    public WorkflowEditorViewModel(
        IWorkflowStorageService storageService,
        ITasksService tasksService,
        BlueprintEditorViewModel blueprintVM,
        IEventService? eventService = null,
        IPluginServer? pluginServer = null,
        BsTextLens? bsTextLens = null,
        ILens<Blueprint, IReadOnlyList<BpEditAction>>? bpGraphLens = null,
        IExecutionBackend? executionBackend = null)
    {
        _storageService = storageService;
        _tasksService = tasksService;
        _eventService = eventService;
        _pluginServer = pluginServer;
        BlueprintVM = blueprintVM;

        // Phase F1: resolve Lens services from DI for BP↔BS round-trip.
        // FT.1: fall back to DI when not passed explicitly (production path).
        _bsTextLens = bsTextLens ?? App.GetService<BsTextLens>();
        _bpGraphLens = bpGraphLens ?? App.GetService<BpGraphLens>();

        // S5: resolve execution backend for BS-mode execution.
        _executionBackend = executionBackend ?? App.GetService<IExecutionBackend>();

        // S5: seed default helper functions for new workflows.
        if (HelperFunctions.Count == 0)
            InitializeDefaultHelperFunctions();

        // S5: BS execution now lives in this VM — forward BlueprintVM output only.
        BlueprintVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BlueprintVM.ExecutionResult))
                ExecutionOutput = BlueprintVM.ExecutionResult;
            if (e.PropertyName == nameof(BlueprintVM.IsExecuting))
                IsExecuting = BlueprintVM.IsExecuting;
            if (e.PropertyName == nameof(BlueprintVM.IsDebugging))
                IsDebugging = BlueprintVM.IsDebugging;
            if (e.PropertyName == nameof(BlueprintVM.IsPaused))
                IsPaused = BlueprintVM.IsPaused;
        };

        // Publish metadata changes immediately so management panel syncs
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(WorkflowName) or nameof(WorkflowDescription) or nameof(WorkflowAuthor))
            {
                IsDirty = true;
                _eventService?.Publish(EventNames.WorkflowDataSaved,
                    new WorkflowSavedEventArgs(
                        _workflowId ?? string.Empty,
                        WorkflowName,
                        WorkflowDescription,
                        WorkflowAuthor));
            }
        };
    }

    /// <summary>
    /// Loads a workflow from storage by ID
    /// </summary>
    public async Task LoadWorkflowAsync(string workflowId)
    {
        _workflowId = workflowId;

        var data = await _storageService.LoadWorkflowDataAsync(workflowId);
        if (data == null)
        {
            Log.Warning("[WorkflowEditorVM] No workflow data found for ID: {Id}", workflowId);
            return;
        }

        WorkflowName = data.Name;
        WorkflowDescription = data.Description ?? string.Empty;
        WorkflowAuthor = data.Author ?? string.Empty;

        // v2: IR is the stored form (data.IrData). Deserialize it into the session,
        // then project BS text on demand for the BS editor. HelperFunctions live in the IR.
        try
        {
            var ir = KitX.Workflow.Serialization.IrSerializer.Deserialize(data.IrData);
            _session = new WorkflowSession(ir)
            {
                HelperFunctions = ir.HelperFunctions.ToList(),
            };
            BlueprintVM.SetSession(_session);

            // BS text is a projection — populate the editor from IR.
            MainProgramCode = _bsTextLens != null
                ? _bsTextLens.Project(ir)
                : string.Empty;

            HelperFunctions.Clear();
            foreach (var func in ir.HelperFunctions)
                HelperFunctions.Add(func);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowEditorVM] Failed to deserialize IR from stored data — editors will be empty");
            MainProgramCode = string.Empty;
        }

        // Mirror helpers into the Blueprint palette so BP mode's Helper Functions
        // panel is populated (replaces the legacy bridge plumbing).
        SyncBlueprintHelperFunctions();

        // Load trigger configuration
        if (data.TriggerConfig != null)
        {
            TriggerType = data.TriggerConfig.TriggerType ?? "Manual";
            TriggerPluginName = data.TriggerConfig.PluginName;
            TriggerName = data.TriggerConfig.TriggerName;
        }
    }

    /// <summary>
    /// <summary>
    /// Saves the current workflow to storage.
    /// If in BP mode, first exports BP→BS so the BlockScript source stays in sync
    /// (runtime executor only reads BlockScript).
    /// </summary>
    public async Task SaveAsync()
    {
        if (_workflowId == null) return;

        // v2: IR (_session.Ir) is the single source of truth — BP edits already flow into
        // it via ApplyBpEdits → SyncService. Save just serializes the session's IR.
        // BS text is a projection, refreshed for the editor display only.
        if (_session != null && _bsTextLens != null && IsBlueprintMode)
        {
            try { MainProgramCode = _bsTextLens.Project(_session.Ir); }
            catch (Exception ex) { Log.Error(ex, "[WorkflowEditorVM] BS projection during save failed"); }
        }

        var data = new KcsFileFormat
        {
            Id = _workflowId,
            Name = WorkflowName,
            Description = WorkflowDescription,
            Author = WorkflowAuthor,
            // v2: store IR (the single source of truth). BS/BP are projections — not persisted.
            IrData = _session != null
                ? KitX.Workflow.Serialization.IrSerializer.Serialize(_session.Ir)
                : "{}",
            VariableConstants = new System.Collections.Generic.Dictionary<string, object?>(),
            TriggerConfig = new TriggerConfig
            {
                TriggerType = TriggerType,
                PluginName = TriggerPluginName,
                TriggerName = TriggerName,
            },
        };

        await _storageService.SaveWorkflowDataAsync(_workflowId, data);
        IsDirty = false;

        // Notify management panel of metadata changes
        _eventService?.Publish(EventNames.WorkflowDataSaved,
            new WorkflowSavedEventArgs(_workflowId, WorkflowName, WorkflowDescription, WorkflowAuthor));
    }

    /// <summary>
    /// Switches to Blueprint mode by converting BS→BP
    /// </summary>
    [RelayCommand]
    private async Task SwitchToBlueprintAsync()
    {
        if (Mode == EditorMode.Blueprint) return;

        // Save current BS state first
        await SaveAsync();

        // Convert BS → BP
        var sourceCode = MainProgramCode ?? string.Empty;
        var helpers = new System.Collections.Generic.List<HelperFunction>(HelperFunctions);

        // Refresh the BP palette with the latest helpers before switching mode.
        SyncBlueprintHelperFunctions();

        if (!string.IsNullOrWhiteSpace(sourceCode))
        {
            try
            {
                // Phase F1: BS→BP via BpGraphLens.Project(session.Ir).
                // Re-parse to refresh the session (BS may have changed since load),
                // then project the IR to a mutable Blueprint for the canvas.
                if (_bsTextLens != null)
                {
                    var ir = _bsTextLens.Parse(sourceCode, helpers);
                    _session = new WorkflowSession(ir) { HelperFunctions = helpers };
                    BlueprintVM.SetSession(_session);
                }

                var blueprint = _session != null && _bpGraphLens != null
                    ? _bpGraphLens.Project(_session.Ir)
                    : null;

                if (blueprint != null)
                {
                    // BS → BP trigger conversion: replace Entry with PluginTriggerNode
                    if (TriggerType == "PluginEvent" && !string.IsNullOrEmpty(TriggerPluginName))
                    {
                        var entryNode = blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
                        if (entryNode != null)
                        {
                            var triggerNode = new PluginTriggerNode
                            {
                                Id = entryNode.Id,
                                X = entryNode.X,
                                Y = entryNode.Y,
                                PluginName = TriggerPluginName ?? string.Empty,
                                TriggerName = TriggerName ?? string.Empty
                            };
                            // Constructor already calls InitializePinsFromDescriptor()

                            // Preserve output pin IDs to maintain connections
                            if (entryNode.OutputPins.Count > 0 && triggerNode.OutputPins.Count > 0)
                                triggerNode.OutputPins[0].Id = entryNode.OutputPins[0].Id;

                            var idx = blueprint.Nodes.IndexOf(entryNode);
                            blueprint.Nodes[idx] = triggerNode;
                        }
                    }

                    BlueprintVM.CurrentBlueprint = blueprint;
                    BlueprintVM.LoadBlueprintIntoDrawing(blueprint);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WorkflowEditorVM] BS→BP conversion failed");
                ExecutionOutput = $"Conversion error: {ex.Message}";
                return;
            }
        }

        Mode = EditorMode.Blueprint;
    }

    /// <summary>
    /// Mirrors the BS-mode helper function list into the Blueprint editor's palette.
    /// S5: helpers now live in this VM directly (no ScriptVM).
    /// </summary>
    private void SyncBlueprintHelperFunctions()
    {
        BlueprintVM.HelperFunctions.Clear();
        foreach (var helper in HelperFunctions)
        {
            BlueprintVM.HelperFunctions.Add(new HelperFunctionPaletteItem
            {
                FunctionName = helper.Name,
                DisplayName = helper.Name,
                Parameters = helper.Parameters ?? [],
                ReturnType = helper.ReturnType ?? "object"
            });
        }
    }

    /// <summary>
    /// Switches to BlockScript mode by converting BP→BS
    /// </summary>
    [RelayCommand]
    private async Task SwitchToBlockScriptAsync()
    {
        if (Mode == EditorMode.BlockScript) return;

        // Convert BP → BS
        if (BlueprintVM.Nodes.Count > 0)
        {
            try
            {
                var blueprint = BlueprintVM.ExportDrawingToBlueprint();

                // BP → BS trigger conversion: extract trigger info from PluginTriggerNode
                var triggerNode = blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.PluginTrigger);
                if (triggerNode is PluginTriggerNode ptNode)
                {
                    TriggerType = "PluginEvent";
                    TriggerPluginName = ptNode.PluginName;
                    TriggerName = ptNode.TriggerName;

                    // Replace PluginTriggerNode with EntryNode for BS conversion compatibility
                    var entryReplacement = new EntryNode
                    {
                        Id = ptNode.Id,
                        X = ptNode.X,
                        Y = ptNode.Y
                    };
                    // Constructor already calls InitializePinsFromDescriptor()

                    // Preserve output pin IDs to maintain connections
                    if (ptNode.OutputPins.Count > 0 && entryReplacement.OutputPins.Count > 0)
                        entryReplacement.OutputPins[0].Id = ptNode.OutputPins[0].Id;

                    var idx = blueprint.Nodes.IndexOf(ptNode);
                    blueprint.Nodes[idx] = entryReplacement;
                }
                else
                {
                    // No PluginTriggerNode → reset to Manual
                    TriggerType = "Manual";
                    TriggerPluginName = null;
                    TriggerName = null;
                }

                // v2: BP→BS is a projection of session.Ir via BsTextLens.
                // The session's IR is already current (BP edits flow into it via ApplyBpEdits).
                var sourceCode = _session != null && _bsTextLens != null
                    ? _bsTextLens.Project(_session.Ir)
                    : "";

                MainProgramCode = sourceCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WorkflowEditorVM] BP→BS conversion failed");
                ExecutionOutput = $"Conversion error: {ex.Message}";
                return;
            }
        }

        await SaveAsync();
        Mode = EditorMode.BlockScript;
    }

    /// <summary>
    /// Shows the Dashboard main window
    /// </summary>
    [RelayCommand]
    private void ShowDashboard()
    {
        Services.UIStateService.MainWindow?.Activate();
    }

    [RelayCommand]
    private void DebugRun() => BlueprintVM.RunWithDebugCommand.Execute(null);

    [RelayCommand]
    private void DebugPause() => BlueprintVM.DebugPauseCommand.Execute(null);

    [RelayCommand]
    private void DebugStep() => BlueprintVM.DebugStepCommand.Execute(null);

    [RelayCommand]
    private void DebugContinue() => BlueprintVM.DebugContinueCommand.Execute(null);

    /// <summary>
    /// Clears the shared execution output panel.
    /// </summary>
    [RelayCommand]
    private void ClearOutput() => ExecutionOutput = string.Empty;

    // ─── S5: migrated ScriptVM commands ─────────────────────────────────

    /// <summary>Adds a new helper function and selects it for editing.</summary>
    [RelayCommand]
    private void AddHelperFunction()
    {
        var newFunction = new HelperFunction
        {
            Name = $"HelperFunction{HelperFunctions.Count + 1}",
            ReturnType = "object",
            Parameters = new List<HelperFunctionParameter>(),
            Code = "// Helper function body\nreturn null;"
        };
        HelperFunctions.Add(newFunction);
        SelectedHelperFunction = newFunction;
    }

    /// <summary>Removes a helper function; adjusts selection if needed.</summary>
    [RelayCommand]
    private void RemoveHelperFunction(HelperFunction? helperFunction)
    {
        if (helperFunction == null) return;
        HelperFunctions.Remove(helperFunction);
        if (SelectedHelperFunction == helperFunction)
            SelectedHelperFunction = HelperFunctions.FirstOrDefault();
    }

    /// <summary>Resets a single constant's UserValue back to its default.</summary>
    [RelayCommand]
    private void ResetConstant(VariableConstant? constant)
    {
        if (constant == null) return;
        constant.UserValue = constant.DefaultValue;
        OnPropertyChanged(nameof(VariableConstants));
    }

    /// <summary>Resets all constants' UserValues back to their defaults.</summary>
    [RelayCommand]
    private void ResetAllConstants()
    {
        foreach (var constant in VariableConstants)
            constant.UserValue = constant.DefaultValue;
        OnPropertyChanged(nameof(VariableConstants));
    }

    /// <summary>Cancels the currently running BS execution.</summary>
    [RelayCommand]
    private void CancelExecution() => _cancellationTokenSource?.Cancel();

    // ─── S5: migrated ScriptVM methods (constants + execution) ──────────

    /// <summary>
    /// Seeds two default helper functions (HelperFuncCompare, HelperFuncAdd)
    /// for new workflows. Migrated verbatim from ScriptVM.
    /// </summary>
    private void InitializeDefaultHelperFunctions()
    {
        HelperFunctions.Add(new HelperFunction
        {
            Name = "HelperFuncCompare",
            ReturnType = "bool",
            Parameters = new List<HelperFunctionParameter>
            {
                new() { Name = "op", Type = "string" },
                new() { Name = "value1", Type = "object?" },
                new() { Name = "value2", Type = "object?" }
            },
            Code = @"var v1 = Convert.ToDouble(value1);
var v2 = Convert.ToDouble(value2);
return op switch
{
    ""BEQ"" => v1 == v2,
    ""BNE"" => v1 != v2,
    ""BLT"" => v1 < v2,
    ""BGT"" => v1 > v2,
    ""BLE"" => v1 <= v2,
    ""BGE"" => v1 >= v2,
    _ => false
};"
        });

        HelperFunctions.Add(new HelperFunction
        {
            Name = "HelperFuncAdd",
            ReturnType = "int",
            Parameters = new List<HelperFunctionParameter>
            {
                new() { Name = "value1", Type = "object?" },
                new() { Name = "value2", Type = "object?" }
            },
            Code = @"var v1 = Convert.ToInt32(value1);
var v2 = Convert.ToInt32(value2);
return v1 + v2;"
        });
    }

    /// <summary>
    /// Parses BS source for constants and updates the VariableConstants list,
    /// preserving any user overrides on constants with matching names.
    /// S5: replaces ScriptVM's IBlockScriptService.ParseConstantsFromBlockScript.
    /// </summary>
    public void ParseConstantsFromCode(string code)
    {
        if (_bsTextLens == null || string.IsNullOrWhiteSpace(code)) return;

        try
        {
            var lowering = _bsTextLens.ParseLowering(code, HelperFunctions);
            var newConstants = new List<VariableConstant>();
            foreach (var (name, irConst) in lowering.Ir.Constants)
            {
                newConstants.Add(new VariableConstant
                {
                    Name = name,
                    Type = irConst.Type,
                    DefaultValue = irConst.DefaultValue,
                    UserValue = irConst.DefaultValue
                });
            }
            UpdateVariableConstants(newConstants);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowEditorVM] ParseConstantsFromCode failed");
        }
    }

    /// <summary>
    /// Rebuilds the VariableConstants list while preserving user-edited UserValues
    /// for constants that still exist. Migrated verbatim from ScriptVM.
    /// </summary>
    private void UpdateVariableConstants(List<VariableConstant> newConstants)
    {
        foreach (var newConstant in newConstants)
        {
            var existing = VariableConstants.FirstOrDefault(c => c.Name == newConstant.Name);
            if (existing != null)
                newConstant.UserValue = existing.UserValue;
        }

        VariableConstants.Clear();
        foreach (var constant in newConstants)
            VariableConstants.Add(constant);
    }

    /// <summary>
    /// Builds a dictionary of user-edited constant values that differ from defaults.
    /// Migrated verbatim from ScriptVM.
    /// </summary>
    private Dictionary<string, object?>? GetUserConstantOverrides()
    {
        if (VariableConstants.Count == 0) return null;

        var overrides = new Dictionary<string, object?>();
        foreach (var constant in VariableConstants)
        {
            if (!object.Equals(constant.UserValue, constant.DefaultValue))
                overrides[constant.Name] = constant.UserValue;
        }

        return overrides.Count > 0 ? overrides : null;
    }

    /// <summary>
    /// Submits the BS source for execution via IExecutionBackend.
    /// S5: replaces ScriptVM's IBlockScriptService.ExecuteBlockScriptAsync.
    /// Validates by attempting ParseLowering; on success applies constant
    /// overrides to the IR (via with-expression) and executes.
    /// </summary>
    internal void SubmitCodes(IDocument doc)
    {
        string codeText;
        try
        {
            codeText = doc.Text;
        }
        catch (InvalidOperationException)
        {
            ExecutionOutput = "Error: Cannot access document from background thread.";
            return;
        }

        // Validation = parse attempt (ParseLowering throws on syntax errors).
        LoweringResult? lowering = null;
        if (_bsTextLens != null)
        {
            try
            {
                lowering = _bsTextLens.ParseLowering(codeText, HelperFunctions);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WorkflowEditorVM] Block script parse failed");
                ExecutionOutput = $"Block script parse failed: {ex.Message}";
                return;
            }
        }

        IsExecuting = true;

        var tokenSource = new CancellationTokenSource();
        _cancellationTokenSource = tokenSource;

        _tasksService.RunTaskAsync(
            async () =>
            {
                string result;
                try
                {
                    var ir = lowering!.Ir;

                    // Apply constant overrides to the immutable IR via with-expression.
                    var overrides = GetUserConstantOverrides();
                    if (overrides != null && overrides.Count > 0)
                    {
                        var builder = ir.Constants.ToBuilder();
                        foreach (var (name, value) in overrides)
                        {
                            if (builder.TryGetValue(name, out var existing))
                                builder[name] = existing with { DefaultValue = value };
                        }
                        ir = ir with { Constants = builder.ToImmutable() };
                    }

                    var executionResult = await _executionBackend.ExecuteAsync(ir, lowering, tokenSource.Token);

                    result = executionResult.IsSuccess
                        ? $"Blocks executed: {executionResult.ExecutedBlockCount}\nExecution time: {executionResult.ExecutionTimeMs}ms\nOutput:\n{string.Join("\n", executionResult.Output)}"
                        : $"Error: {executionResult.ErrorMessage}";
                }
                catch (OperationCanceledException)
                {
                    result = "Execution cancelled.";
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                }
                finally
                {
                    tokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                Dispatcher.UIThread.Invoke(() =>
                {
                    ExecutionOutput = result;
                    IsExecuting = false;
                });
            },
            tokenSource.Token,
            nameof(SubmitCodes));
    }
}
