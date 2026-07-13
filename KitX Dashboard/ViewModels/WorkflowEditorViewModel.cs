using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
using KitX.Workflow.Session;
using KitX.Workflow.Lens;
using KitX.Workflow.Lens.BsTextLens;
using KitX.Workflow.Lens.BpGraphLens;
using Serilog;
using BsTextLens = KitX.Workflow.Lens.BsTextLens.BsTextLens;
using BpGraphLens = KitX.Workflow.Lens.BpGraphLens.BpGraphLens;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Unified workflow editor ViewModel that coordinates BS and BP editing modes.
/// Uses composition: holds references to existing ScriptVM and BlueprintVM.
/// </summary>
internal partial class WorkflowEditorViewModel : ObservableObject
{
    public enum EditorMode { BlockScript, Blueprint }

    private readonly IWorkflowStorageService _storageService;
    private readonly ITasksService _tasksService;
    private readonly IEventService? _eventService;
    private readonly IPluginServer? _pluginServer;

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

    /// <summary>
    /// The BS editor sub-ViewModel
    /// </summary>
    public WorkflowScriptEditorWindowViewModel ScriptVM { get; }

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
        WorkflowScriptEditorWindowViewModel scriptVM,
        BlueprintEditorViewModel blueprintVM,
        IEventService? eventService = null,
        IPluginServer? pluginServer = null,
        BsTextLens? bsTextLens = null,
        ILens<Blueprint, IReadOnlyList<BpEditAction>>? bpGraphLens = null)
    {
        _storageService = storageService;
        _tasksService = tasksService;
        _eventService = eventService;
        _pluginServer = pluginServer;
        ScriptVM = scriptVM;
        BlueprintVM = blueprintVM;

        // Phase F1: resolve Lens services from DI for BP↔BS round-trip.
        // FT.1: fall back to DI when not passed explicitly (production path).
        _bsTextLens = bsTextLens ?? App.GetService<BsTextLens>();
        _bpGraphLens = bpGraphLens ?? App.GetService<BpGraphLens>();

        // Forward execution output from sub-VMs
        ScriptVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ScriptVM.ExecutionResult))
                ExecutionOutput = ScriptVM.ExecutionResult;
            if (e.PropertyName == nameof(ScriptVM.IsExecuting))
                IsExecuting = ScriptVM.IsExecuting;
        };

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
            ScriptVM.MainProgramCode = _bsTextLens != null
                ? _bsTextLens.Project(ir)
                : string.Empty;

            ScriptVM.HelperFunctions.Clear();
            foreach (var func in ir.HelperFunctions)
                ScriptVM.HelperFunctions.Add(func);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowEditorVM] Failed to deserialize IR from stored data — editors will be empty");
            ScriptVM.MainProgramCode = string.Empty;
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
            try { ScriptVM.MainProgramCode = _bsTextLens.Project(_session.Ir); }
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
        var sourceCode = ScriptVM.MainProgramCode ?? string.Empty;
        var helpers = new System.Collections.Generic.List<HelperFunction>(ScriptVM.HelperFunctions);

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
    /// Replaces the legacy SetBridge → RefreshHelperFunctions path: the unified editor
    /// holds both sub-VMs directly, so we copy from ScriptVM whenever helpers change.
    /// </summary>
    private void SyncBlueprintHelperFunctions()
    {
        BlueprintVM.HelperFunctions.Clear();
        foreach (var helper in ScriptVM.HelperFunctions)
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

                ScriptVM.MainProgramCode = sourceCode;
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
}
