using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Tasks;
using KitX.Core.Event;
using KitX.Core.Tasks;
using KitX.Dashboard;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using Serilog;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Unified workflow editor ViewModel that coordinates BS and BP editing modes.
/// Uses composition: holds references to existing ScriptVM and BlueprintVM.
/// </summary>
internal partial class WorkflowEditorViewModel : ObservableObject
{
    public enum EditorMode { BlockScript, Blueprint }

    private readonly IWorkflowStorageService _storageService;
    private readonly IBlueprintService _blueprintService;
    private readonly ITasksService _tasksService;
    private readonly IEventService _eventService;

    private EditorMode _mode = EditorMode.BlockScript;
    private string? _workflowId;
    private string _workflowName = "Untitled Workflow";
    private string _workflowDescription = string.Empty;
    private string _workflowAuthor = string.Empty;
    private bool _isDirty;
    private string _executionOutput = string.Empty;
    private bool _isExecuting;

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
        var pluginServer = KitX.Core.DI.ServiceHost.GetRequiredService<KitX.Core.Contract.Plugin.IPluginServer>();
        if (pluginServer == null) return;

        foreach (var conn in pluginServer.Connections)
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

        var pluginServer = KitX.Core.DI.ServiceHost.GetRequiredService<KitX.Core.Contract.Plugin.IPluginServer>();
        if (pluginServer == null) return;

        var conn = pluginServer.Connections
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

    public WorkflowEditorViewModel(
        IWorkflowStorageService storageService,
        IBlueprintService blueprintService,
        ITasksService tasksService,
        WorkflowScriptEditorWindowViewModel scriptVM,
        BlueprintEditorViewModel blueprintVM)
    {
        _storageService = storageService;
        _blueprintService = blueprintService;
        _tasksService = tasksService;
        _eventService = App.GetService<IEventService>();
        ScriptVM = scriptVM;
        BlueprintVM = blueprintVM;

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
        };

        // Publish metadata changes immediately so management panel syncs
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(WorkflowName) or nameof(WorkflowDescription) or nameof(WorkflowAuthor))
            {
                IsDirty = true;
                _eventService.Publish(EventNames.WorkflowDataSaved,
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

        // Load into BS editor
        ScriptVM.UseBlockMode = data.UseBlockMode;
        ScriptVM.MainProgramCode = data.UseBlockMode
            ? (data.BlockScriptSource ?? string.Empty)
            : data.MainProgram;

        ScriptVM.HelperFunctions.Clear();
        foreach (var func in data.HelperFunctions)
            ScriptVM.HelperFunctions.Add(func);

        // Load into BP editor if blueprint data exists
        if (data.BlueprintData != null)
        {
            BlueprintVM.CurrentBlueprint = data.BlueprintData;
        }

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

        // If in BP mode, sync BP→BS so runtime executor has up-to-date BlockScript
        if (IsBlueprintMode && BlueprintVM.Nodes.Count > 0)
        {
            try
            {
                var blueprint = BlueprintVM.ExportDrawingToBlueprint();

                // Handle trigger node → Entry replacement for conversion (same as SwitchToBlockScriptAsync)
                var triggerNode = blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.PluginTrigger);
                if (triggerNode is PluginTriggerNode ptNode)
                {
                    var entryReplacement = new EntryNode
                    {
                        Id = ptNode.Id,
                        X = ptNode.X,
                        Y = ptNode.Y
                    };
                    if (ptNode.OutputPins.Count > 0 && entryReplacement.OutputPins.Count > 0)
                        entryReplacement.OutputPins[0].Id = ptNode.OutputPins[0].Id;
                    var idx = blueprint.Nodes.IndexOf(ptNode);
                    blueprint.Nodes[idx] = entryReplacement;
                }

                var sourceCode = _blueprintService.ExportToBlockScript(blueprint);
                ScriptVM.MainProgramCode = sourceCode;
                ScriptVM.UseBlockMode = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WorkflowEditorVM] BP→BS sync during save failed");
            }
        }

        var data = new KcsFileFormat
        {
            Id = _workflowId,
            Name = WorkflowName,
            Description = WorkflowDescription,
            Author = WorkflowAuthor,
            UseBlockMode = ScriptVM.UseBlockMode,
            BlockScriptSource = ScriptVM.UseBlockMode ? ScriptVM.MainProgramCode : null,
            MainProgram = ScriptVM.UseBlockMode ? string.Empty : (ScriptVM.MainProgramCode ?? string.Empty),
            HelperFunctions = new System.Collections.Generic.List<HelperFunction>(ScriptVM.HelperFunctions),
            VariableConstants = ScriptVM.UseBlockMode
                ? new System.Collections.Generic.Dictionary<string, object?>()
                : new System.Collections.Generic.Dictionary<string, object?>(),
            BlueprintData = BlueprintVM.CurrentBlueprint,
            TriggerConfig = new TriggerConfig
            {
                TriggerType = TriggerType,
                PluginName = TriggerPluginName,
                TriggerName = TriggerName
            },
        };

        await _storageService.SaveWorkflowDataAsync(_workflowId, data);
        IsDirty = false;

        // Notify management panel of metadata changes
        _eventService.Publish(EventNames.WorkflowDataSaved,
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

        if (!string.IsNullOrWhiteSpace(sourceCode))
        {
            try
            {
                var blueprint = _blueprintService.ImportFromBlockScript(sourceCode, helpers);
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

                var sourceCode = _blueprintService.ExportToBlockScript(blueprint);

                ScriptVM.MainProgramCode = sourceCode;
                ScriptVM.UseBlockMode = true;
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
}
