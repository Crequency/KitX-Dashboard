using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using KitX.Core.Event;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using Serilog;
using V6Workflow = KitX.WorkflowV6.Ir.Workflow;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowEditorViewModelV6 — v6 workflow editor with full KS-side feature parity.
//
// Mirrors WorkflowEditorViewModel (v5.1) at the feature level: mode switching,
// metadata, Trigger config, Helper Function management, Variable Constants panel,
// and KS↔BP conversion via KsTextLens/BpGraphLens. Differences from v5.1:
//   • KsTextLens (v6 indented grammar) instead of BsTextLens (v5 block grammar)
//   • WorkflowSerializer (v6 IR) instead of IrSerializer (v5 IR)
//   • V6 Constant record uses InitialValueExpression (not V5 IrConstant.DefaultValue)
//   • IrVersion = "v6" in KcsFileFormat
//   • Run/Save use v6 backend (P4); currently stubs for Run, basic save implemented
// ─────────────────────────────────────────────────────────────────────────────

internal partial class WorkflowEditorViewModelV6 : ObservableObject
{
    public enum EditorMode { BlockScript, Blueprint }

    private readonly IWorkflowStorageService? _storageService;
    private readonly IEventService? _eventService;
    private readonly IPluginServer? _pluginServer;
    private readonly KsTextLens _ksTextLens;
    private readonly BpGraphLens _bpGraphLens;

    private EditorMode _mode = EditorMode.BlockScript;
    private string? _workflowId;
    private string _workflowName = "Untitled Workflow (v6)";
    private string _workflowDescription = string.Empty;
    private string _workflowAuthor = string.Empty;
    private TriggerConfig? _triggerConfig;
    private bool _isDirty;
    private string _executionOutput = string.Empty;
    private bool _isExecuting;
    private string _statusText = "Ready (v6)";
    private string _ksSource = string.Empty;
    private string _conversionError = string.Empty;
    private V6Workflow? _lastIr;

    // ─── Trigger Configuration ──────────────────────────────────────────
    private string _triggerType = "Manual";
    private string? _triggerPluginName;
    private string? _triggerName;

    // ─── Helper Function ───────────────────────────────────────────────
    private HelperFunction? _selectedHelperFunction;
    private ObservableCollection<HelperFunction> _helperFunctions = [];
    private ObservableCollection<HelperFunctionParameter> _parameters = [];
    private bool _isEditingHelperFunction;

    // ─── Variable Constants ─────────────────────────────────────────────
    private ObservableCollection<VariableConstant> _variableConstants = [];

    public static string DefaultSource { get; } =
        "const {" + Environment.NewLine +
        "    int max = 3" + Environment.NewLine +
        "}" + Environment.NewLine +
        Environment.NewLine +
        "var {" + Environment.NewLine +
        "    int counter" + Environment.NewLine +
        "    int sum" + Environment.NewLine +
        "}" + Environment.NewLine +
        Environment.NewLine +
        "Print(\"hello, v6\")" + Environment.NewLine +
        "0 > counter" + Environment.NewLine +
        "0 > sum" + Environment.NewLine +
        "forEach Range(0, max, 1) as i:" + Environment.NewLine +
        "    counter > Add(_, 1) > counter" + Environment.NewLine +
        "    counter > sum";

    public WorkflowEditorViewModelV6(KsTextLens ksTextLens, BpGraphLens bpGraphLens)
    {
        _ksTextLens = ksTextLens ?? throw new ArgumentNullException(nameof(ksTextLens));
        _bpGraphLens = bpGraphLens ?? throw new ArgumentNullException(nameof(bpGraphLens));

        BuiltinFunctionRegistry? registry = null;
        try { registry = App.GetService<BuiltinFunctionRegistry>(); } catch { /* host without DI */ }
        BlueprintVM = new BlueprintEditorViewModelV6(_bpGraphLens, registry);

        _ksSource = DefaultSource;

        try
        {
            _storageService = App.GetService<IWorkflowStorageService>();
            _eventService = App.GetService<IEventService>();
            _pluginServer = App.GetService<IPluginServer>();
        }
        catch { /* test/host without DI — OK */ }

        // Seed default helper functions (same as v5.1)
        if (HelperFunctions.Count == 0)
            InitializeDefaultHelperFunctions();

        // Publish metadata changes
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(WorkflowName) or nameof(WorkflowDescription) or nameof(WorkflowAuthor))
            {
                IsDirty = true;
                _eventService?.Publish(EventNames.WorkflowDataSaved,
                    new WorkflowSavedEventArgs(
                        _workflowId ?? string.Empty,
                        WorkflowName, WorkflowDescription, WorkflowAuthor));
            }
        };
    }

    // ── Mode ──

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

    // ── Metadata ──

    public string WorkflowName { get => _workflowName; set => SetProperty(ref _workflowName, value); }
    public string WorkflowDescription { get => _workflowDescription; set => SetProperty(ref _workflowDescription, value); }
    public string WorkflowAuthor { get => _workflowAuthor; set => SetProperty(ref _workflowAuthor, value); }
    public bool IsDirty { get => _isDirty; set => SetProperty(ref _isDirty, value); }
    public string ExecutionOutput { get => _executionOutput; set => SetProperty(ref _executionOutput, value); }
    public bool IsExecuting { get => _isExecuting; set => SetProperty(ref _isExecuting, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    // ── KS Source ──

    public string KsSource
    {
        get => _ksSource;
        set { if (SetProperty(ref _ksSource, value)) IsDirty = true; }
    }

    public string ConversionError
    {
        get => _conversionError;
        set => SetProperty(ref _conversionError, value);
    }

    // ── Trigger ──

    public string[] TriggerTypeOptions { get; } = ["Manual", "PluginEvent"];
    public ObservableCollection<string> AvailablePlugins { get; } = [];
    public ObservableCollection<string> AvailableTriggers { get; } = [];

    public string TriggerType
    {
        get => _triggerType;
        set
        {
            if (SetProperty(ref _triggerType, value))
            {
                OnPropertyChanged(nameof(IsPluginEventTrigger));
                if (value == "PluginEvent") RefreshAvailablePlugins();
                IsDirty = true;
            }
        }
    }

    public bool IsPluginEventTrigger => _triggerType == "PluginEvent";

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

    public string? TriggerName
    {
        get => _triggerName;
        set { if (SetProperty(ref _triggerName, value)) IsDirty = true; }
    }

    private void RefreshAvailablePlugins()
    {
        AvailablePlugins.Clear();
        if (_pluginServer == null) return;
        foreach (var conn in _pluginServer.Connections)
            if (!string.IsNullOrEmpty(conn.PluginInfo?.Name))
                AvailablePlugins.Add(conn.PluginInfo.Name);
    }

    private void RefreshAvailableTriggers()
    {
        AvailableTriggers.Clear();
        if (string.IsNullOrEmpty(_triggerPluginName) || _pluginServer == null) return;
        var conn = _pluginServer.Connections
            .FirstOrDefault(c => c.PluginInfo?.Name == _triggerPluginName);
        if (conn?.PluginInfo?.SupportedTriggers == null) return;
        foreach (var trigger in conn.PluginInfo.SupportedTriggers)
            AvailableTriggers.Add(trigger);
    }

    // ── Helper Functions ──

    public HelperFunction? SelectedHelperFunction
    {
        get => _selectedHelperFunction;
        set
        {
            if (SetProperty(ref _selectedHelperFunction, value))
            {
                IsEditingHelperFunction = value != null;
                Parameters.Clear();
                if (value?.Parameters != null)
                    foreach (var p in value.Parameters) Parameters.Add(p);
            }
        }
    }

    public ObservableCollection<HelperFunction> HelperFunctions
    {
        get => _helperFunctions;
        set => SetProperty(ref _helperFunctions, value);
    }

    public ObservableCollection<HelperFunctionParameter> Parameters
    {
        get => _parameters;
        set => SetProperty(ref _parameters, value);
    }

    public bool IsEditingHelperFunction
    {
        get => _isEditingHelperFunction;
        set => SetProperty(ref _isEditingHelperFunction, value);
    }

    [RelayCommand]
    private void AddHelperFunction()
    {
        var fn = new HelperFunction
        {
            Name = $"HelperFunction{HelperFunctions.Count + 1}",
            ReturnType = "object",
            Parameters = new List<HelperFunctionParameter>(),
            Code = "// Helper function body\nreturn null;"
        };
        HelperFunctions.Add(fn);
        SelectedHelperFunction = fn;
    }

    [RelayCommand]
    private void RemoveHelperFunction(HelperFunction? fn)
    {
        if (fn == null) return;
        HelperFunctions.Remove(fn);
        if (SelectedHelperFunction == fn)
            SelectedHelperFunction = HelperFunctions.FirstOrDefault();
    }

    private void InitializeDefaultHelperFunctions()
    {
        HelperFunctions.Add(new HelperFunction
        {
            Name = "Compare",
            ReturnType = "bool",
            Parameters = new List<HelperFunctionParameter>
            {
                new() { Name = "op", Type = "string" },
                new() { Name = "a", Type = "object?" },
                new() { Name = "b", Type = "object?" }
            },
            Code = "var v1 = Convert.ToDouble(a);\nvar v2 = Convert.ToDouble(b);\nreturn op switch\n{\n    \"BEQ\" => v1 == v2,\n    \"BNE\" => v1 != v2,\n    \"BLT\" => v1 < v2,\n    \"BGT\" => v1 > v2,\n    \"BLE\" => v1 <= v2,\n    \"BGE\" => v1 >= v2,\n    _ => false\n};"
        });
        HelperFunctions.Add(new HelperFunction
        {
            Name = "Add",
            ReturnType = "int",
            Parameters = new List<HelperFunctionParameter>
            {
                new() { Name = "a", Type = "object?" },
                new() { Name = "b", Type = "object?" }
            },
            Code = "var v1 = Convert.ToInt32(a);\nvar v2 = Convert.ToInt32(b);\nreturn v1 + v2;"
        });
    }

    // ── Variable Constants ──

    public ObservableCollection<VariableConstant> VariableConstants
    {
        get => _variableConstants;
        set => SetProperty(ref _variableConstants, value);
    }

    [RelayCommand]
    private void ResetConstant(VariableConstant? constant)
    {
        if (constant == null) return;
        constant.UserValue = constant.DefaultValue;
        OnPropertyChanged(nameof(VariableConstants));
    }

    [RelayCommand]
    private void ResetAllConstants()
    {
        foreach (var c in VariableConstants) c.UserValue = c.DefaultValue;
        OnPropertyChanged(nameof(VariableConstants));
    }

    /// <summary>
    /// Parses KS source for const/var declarations and updates the VariableConstants
    /// list, preserving any user overrides on constants with matching names.
    /// </summary>
    public void ParseConstantsFromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        try
        {
            var ir = _ksTextLens.Parse(code, HelperFunctions);
            var newConstants = new List<VariableConstant>();
            foreach (var (name, c) in ir.Constants)
            {
                newConstants.Add(new VariableConstant
                {
                    Name = name,
                    Type = c.Type,
                    DefaultValue = c.InitialValueExpression,
                    UserValue = c.InitialValueExpression
                });
            }
            // Also show var declarations (editable initial values for v6)
            foreach (var (name, v) in ir.GlobalVars)
            {
                newConstants.Add(new VariableConstant
                {
                    Name = name,
                    Type = v.Type,
                    DefaultValue = v.InitialValueExpression,
                    UserValue = v.InitialValueExpression
                });
            }
            UpdateVariableConstants(newConstants);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowEditorVMV6] ParseConstantsFromCode failed");
        }
    }

    private void UpdateVariableConstants(List<VariableConstant> newConstants)
    {
        foreach (var nc in newConstants)
        {
            var existing = VariableConstants.FirstOrDefault(c => c.Name == nc.Name);
            if (existing != null) nc.UserValue = existing.UserValue;
        }
        VariableConstants.Clear();
        foreach (var c in newConstants) VariableConstants.Add(c);
    }

    // ── Blueprint VM ──

    public BlueprintEditorViewModelV6 BlueprintVM { get; private set; } = null!;

    // ── Mode switch ──

    [RelayCommand]
    private void SwitchToBlockScript()
    {
        if (_mode == EditorMode.Blueprint)
        {
            try
            {
                // BP→IR→KS: reverse the (possibly edited) working blueprint.
                var bp = BlueprintVM.WorkingBlueprint;
                if (bp != null && bp.Nodes.Count > 0)
                {
                    var ir = _bpGraphLens.Reverse(bp);
                    _lastIr = ir;
                    KsSource = _ksTextLens.Project(ir);
                }
                else if (_lastIr != null)
                {
                    KsSource = _ksTextLens.Project(_lastIr);
                }
                ConversionError = string.Empty;
            }
            catch (Exception ex)
            {
                ConversionError = $"BP→KS 转换失败: {ex.Message}";
                Log.Warning(ex, "[WorkflowEditorVMV6] BP→KS reverse failed");
            }
        }
        Mode = EditorMode.BlockScript;
    }

    [RelayCommand]
    private void SwitchToBlueprint()
    {
        if (_mode == EditorMode.BlockScript)
            RenderBlueprintFromKs();
        Mode = EditorMode.Blueprint;
    }

    private void RenderBlueprintFromKs()
    {
        try
        {
            var helpers = new List<HelperFunction>(HelperFunctions);
            var ir = _ksTextLens.Parse(KsSource, helpers);
            _lastIr = ir;

            var bp = _bpGraphLens.Project(ir);
            var scopes = _bpGraphLens.AnalyzeScopes(bp);

            BlueprintVM.LoadBlueprint(bp, scopes);
            ConversionError = string.Empty;
            StatusText = $"BP 渲染完成: {bp.Nodes.Count} 节点, {bp.Connections.Count} 连线";
        }
        catch (Exception ex)
        {
            ConversionError = $"KS→BP 转换失败: {ex.Message}";
            StatusText = "KS 解析错误";
            Log.Warning(ex, "[WorkflowEditorVMV6] KS→BP conversion failed");
        }
    }

    // ── Load from .kcs ──

    /// <summary>
    /// Loads a v6 Workflow IR (deserialised from a .kcs file's IrData), renders it
    /// to KS text, restores metadata/trigger/helper functions.
    /// </summary>
    public void LoadFromIr(V6Workflow ir, string name, KcsFileFormat? kcs)
    {
        _lastIr = ir;
        KsSource = _ksTextLens.Project(ir);
        WorkflowName = name;
        IsDirty = false;
        StatusText = "Loaded (v6)";

        // Restore helper functions from IR
        HelperFunctions.Clear();
        foreach (var fn in ir.HelperFunctions)
            HelperFunctions.Add(fn);

        // Parse constants from the loaded source
        ParseConstantsFromCode(KsSource);

        // Restore trigger config
        if (kcs?.TriggerConfig != null)
        {
            _triggerConfig = kcs.TriggerConfig;
            TriggerType = kcs.TriggerConfig.TriggerType ?? "Manual";
            TriggerPluginName = kcs.TriggerConfig.PluginName;
            TriggerName = kcs.TriggerConfig.TriggerName;
        }
        else
        {
            _triggerConfig = new TriggerConfig();
        }

        // Restore metadata
        if (kcs != null)
        {
            WorkflowDescription = kcs.Description ?? string.Empty;
            WorkflowAuthor = kcs.Author ?? string.Empty;
        }
    }

    // ── Save ──

    /// <summary>
    /// Saves the current workflow to storage as a v6 .kcs file.
    /// Parses current KS source into V6 IR, serialises via WorkflowSerializer.
    /// </summary>
    public async Task SaveAsync()
    {
        if (_workflowId == null || _storageService == null) return;

        try
        {
            var helpers = new List<HelperFunction>(HelperFunctions);
            V6Workflow ir;
            if (_mode == EditorMode.Blueprint && BlueprintVM.WorkingBlueprint != null
                && BlueprintVM.WorkingBlueprint.Nodes.Count > 0)
            {
                // BP mode: reverse the edited blueprint instead of re-parsing stale KS text.
                ir = _bpGraphLens.Reverse(BlueprintVM.WorkingBlueprint);
            }
            else
            {
                ir = _ksTextLens.Parse(KsSource, helpers);
            }
            _lastIr = ir;

            var irData = KitX.WorkflowV6.Serialization.WorkflowSerializer.Serialize(ir);

            var tc = _triggerConfig ?? new TriggerConfig();
            tc.TriggerType = TriggerType;
            tc.PluginName = TriggerPluginName;
            tc.TriggerName = TriggerName;

            var data = new KcsFileFormat
            {
                Id = _workflowId,
                Name = WorkflowName,
                Description = WorkflowDescription,
                Author = WorkflowAuthor,
                IrData = irData,
                IrVersion = "v6",
                VariableConstants = new Dictionary<string, object?>(),
                TriggerConfig = tc,
            };

            await _storageService.SaveWorkflowDataAsync(_workflowId, data);
            IsDirty = false;

            _eventService?.Publish(EventNames.WorkflowDataSaved,
                new WorkflowSavedEventArgs(_workflowId, WorkflowName, WorkflowDescription, WorkflowAuthor));

            StatusText = "Saved (v6)";
            Log.Information("[WorkflowEditorVMV6] Saved workflow: {Id}", _workflowId);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            Log.Error(ex, "[WorkflowEditorVMV6] SaveAsync failed");
        }
    }

    /// <summary>Sets the workflow ID (called by LoadWorkflowAsync in the window code-behind).</summary>
    internal void SetWorkflowId(string id) => _workflowId = id;

    // ── ShowDashboard ──

    [RelayCommand]
    private void ShowDashboard()
    {
        Services.UIStateService.MainWindow?.Activate();
    }

    // ── Toolbar stubs (P4) ──

    [RelayCommand]
    private void Run()
    {
        ExecutionOutput = "[v6] Run 尚未接入。执行后端 (StructuredRoslynBackend) 将在 P4 阶段接入。";
        Log.Information("[WorkflowEditorVMV6] Run invoked (stub — P4)");
    }

    [RelayCommand]
    private void Stop()
    {
        Log.Information("[WorkflowEditorVMV6] Stop invoked (stub)");
    }

    [RelayCommand]
    private void DebugRun()
    {
        ExecutionOutput = "[v6] Debug 尚未接入。调试高亮通道将在 P4 阶段接入。";
        Log.Information("[WorkflowEditorVMV6] DebugRun invoked (stub — P4)");
    }

    [RelayCommand]
    private void ClearOutput() => ExecutionOutput = string.Empty;
}