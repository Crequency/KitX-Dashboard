using System;
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
    }

    /// <summary>
    /// Saves the current workflow to storage
    /// </summary>
    public async Task SaveAsync()
    {
        if (_workflowId == null) return;

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
