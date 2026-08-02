using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using KitX.Core.Event;
using KitX.Dashboard.Services;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;
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
    private readonly StructuredRoslynBackend? _executionBackend;
    private CancellationTokenSource? _cancellationTokenSource;
    private RealBlueprintDebugger? _debugController;
    private bool _isDebugging;
    private bool _isPaused;

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
        "forEach max > Range(0, _, 1) as i:" + Environment.NewLine +
        "    counter > Add(_, 1) > counter" + Environment.NewLine +
        "    counter > sum";

    public WorkflowEditorViewModelV6(KsTextLens ksTextLens, BpGraphLens bpGraphLens)
    {
        _ksTextLens = ksTextLens ?? throw new ArgumentNullException(nameof(ksTextLens));
        _bpGraphLens = bpGraphLens ?? throw new ArgumentNullException(nameof(bpGraphLens));

        BuiltinFunctionRegistry? registry = null;
        try { registry = App.GetService<BuiltinFunctionRegistry>(); } catch { /* host without DI */ }
        IPluginServer? pluginServer = null;
        try { pluginServer = App.GetService<IPluginServer>(); } catch { /* host without DI */ }
        BlueprintVM = new BlueprintEditorViewModelV6(_bpGraphLens, registry, pluginServer);

        // Resolve the v6 execution backend (shared interface points elsewhere; use concrete type).
        try { _executionBackend = App.GetService<StructuredRoslynBackend>(); } catch { /* host without DI */ }

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
    public bool IsDebugging { get => _isDebugging; set => SetProperty(ref _isDebugging, value); }
    public bool IsPaused { get => _isPaused; set => SetProperty(ref _isPaused, value); }
    public ObservableCollection<RuntimeVariableItem> RuntimeVariables { get; } = [];

    // ── KS Source ──

    public string KsSource
    {
        get => _ksSource;
        set
        {
            if (SetProperty(ref _ksSource, value))
            {
                IsDirty = true;
                // Log the full KS source — KScript documents are short, truncating adds
                // no value and hides the tail that users are actually editing.
                Log.Information("[WorkflowEditorVMV6] KsSource set: {Length} chars, full source:\n{Preview}",
                    value?.Length ?? 0, value ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// Provides the current editor document text from the view (R1). Returns null while the
    /// editor is showing a Helper Function rather than the main program — Save/Run/Switch use
    /// this to take an explicit snapshot instead of trusting the TextChanged-maintained cache.
    /// </summary>
    public Func<string?>? EditorTextProvider { get; set; }

    /// <summary>
    /// Snapshot the editor document into <see cref="KsSource"/> when the editor is in the
    /// main-program context (R1). Eliminates the "stale cache" failure mode where a helper
    /// editing session leaves KsSource holding an old value.
    /// </summary>
    private void SyncFromEditorText()
    {
        var text = EditorTextProvider?.Invoke();
        if (text != null)
            KsSource = text;
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

    // ── Constant overrides (P4-α) ──

    /// <summary>
    /// Collects user-modified constant/var overrides (UserValue != DefaultValue).
    /// Returns null when nothing is overridden. Unlike v5.1 which compares
    /// evaluated objects, v6 compares the raw text (InitialValueExpression strings).
    /// </summary>
    private Dictionary<string, string?>? GetUserConstantOverridesV6()
    {
        if (VariableConstants.Count == 0) return null;

        var overrides = new Dictionary<string, string?>();
        foreach (var constant in VariableConstants)
        {
            var def = constant.DefaultValue?.ToString();
            var usr = constant.UserValue?.ToString();
            if (!string.Equals(def, usr, StringComparison.Ordinal))
                overrides[constant.Name] = usr;
        }
        return overrides.Count > 0 ? overrides : null;
    }

    // ── BP ↔ Variable Constants panel sync (2026-08-02) ──
    //
    // BP definition nodes carry the KS-script initialiser as a READ-ONLY DefaultValue
    // and a USER value (ConstValue / VarInitialValue) that is the BP-side counterpart
    // of the KS editor's Variable Constants panel UserValue. The two are synced on
    // mode switches / save / run so overrides survive without ever rewriting the KS
    // script text (the script keeps its defaults).

    /// <summary>
    /// True for a definition node: the standalone declaration (no connections).
    /// Usage nodes (same name, wired into data/exec edges) must be excluded — they
    /// carry no user value and would otherwise clobber the definition's override.
    /// </summary>
    private static bool IsDefinitionNode(Blueprint bp, BlueprintNode node)
        => !bp.Connections.Any(c => c.SourceNodeId == node.Id || c.TargetNodeId == node.Id);

    /// <summary>
    /// BP→panel: copies user values from definition nodes into the Variable Constants
    /// panel (UserValue). Definition nodes absent from the panel are added with their
    /// default (usually empty — the user created them on the BP side).
    /// </summary>
    private void SyncUserValuesFromBlueprint(Blueprint bp)
    {
        if (bp == null) return;
        Log.Information("[WFEVM] SyncUserValuesFromBlueprint: {Nodes} nodes, panel has {Consts} entries",
            bp.Nodes.Count, VariableConstants.Count);
        foreach (var node in bp.Nodes)
        {
            if (!IsDefinitionNode(bp, node)) continue;
            string? name = null, type = null, defaultValue = null, userValue = null;
            switch (node)
            {
                case ConstNode cn:
                    name = cn.ConstName; type = cn.ConstType;
                    defaultValue = cn.DefaultValue; userValue = cn.ConstValue;
                    break;
                case VariableNode vn when vn.VarKind == VariableKind.PubVar:
                    name = vn.VarName; type = vn.VarType;
                    defaultValue = vn.DefaultValue; userValue = vn.VarInitialValue;
                    break;
            }
            if (string.IsNullOrEmpty(name)) continue;

            var existing = VariableConstants.FirstOrDefault(c => c.Name == name);
            if (existing != null)
            {
                // Always mirror the node's value — a cleared user value falls back to
                // the default (no override), otherwise the panel keeps a stale value.
                existing.UserValue = userValue ?? existing.DefaultValue;
                Log.Information("[WFEVM] Sync: {Name} nodeUser={User} → panel UserValue={Panel}",
                    name, userValue, existing.UserValue);
            }
            else
            {
                VariableConstants.Add(new VariableConstant
                {
                    Name = name,
                    Type = type ?? "object",
                    DefaultValue = defaultValue,
                    UserValue = userValue,
                });
                Log.Information("[WFEVM] Sync: {Name} added to panel (user={User})", name, userValue);
            }
        }
    }

    /// <summary>
    /// Panel→BP: mirrors the Variable Constants panel (UserValue, the effective
    /// initial value) onto the freshly rebuilt definition nodes, so switching KS→BP
    /// shows the user's override on the BP canvas. Written unconditionally — a
    /// cleared panel value clears the node's user value (falls back to the default).
    /// </summary>
    private void RestoreUserValuesFromPanel(Blueprint bp)
    {
        if (bp == null) return;
        Log.Information("[WFEVM] RestoreUserValuesFromPanel: {Consts} constants → {Nodes} nodes",
            VariableConstants.Count, bp.Nodes.Count);
        foreach (var constant in VariableConstants)
        {
            var usr = constant.UserValue?.ToString();
            Log.Information("[WFEVM] Restore: {Name} UserValue={User}", constant.Name, usr);
            foreach (var node in bp.Nodes)
            {
                // Only the standalone definition node (no connections) carries the
                // user value; usage nodes must stay untouched.
                if (!IsDefinitionNode(bp, node)) continue;
                if (node is ConstNode cn && cn.ConstName == constant.Name)
                {
                    cn.ConstValue = usr;
                    Log.Information("[WFEVM] Restore: const {Name} → ConstValue={User}", constant.Name, usr);
                }
                else if (node is VariableNode vn && vn.VarName == constant.Name && vn.VarKind == VariableKind.PubVar)
                {
                    vn.VarInitialValue = usr;
                    Log.Information("[WFEVM] Restore: var {Name} → VarInitialValue={User}", constant.Name, usr);
                }
            }
        }
    }

    // ── KS parse validation (diagnostics-aware) ──

    /// <summary>
    /// Parses <see cref="KsSource"/> and fails fast when the tokenizer/parser reports
    /// errors (KS001/KS002/KS062 etc.). The parser is lenient — it skips offending
    /// lines (dropping const/var/body content) but still returns a partial IR. This
    /// helper surfaces those errors so Run/Save/BP-switch do NOT silently consume a
    /// damaged IR. Returns the formatted error text, or null when the source parses clean.
    /// </summary>
    private string? ValidateKsParse()
    {
        if (string.IsNullOrWhiteSpace(KsSource)) return null;
        try
        {
            var (_, diag) = _ksTextLens.ParseAstWithDiagnostics(KsSource);
            if (!diag.HasErrors) return null;
            var detail = string.Join("\n", diag.Items
                .Where(d => d.Severity == KsDiagnosticSeverity.Error)
                .Select(d => $"  [{d.Code}] L{d.Line}: {d.Message}"));
            Log.Warning("[WorkflowEditorVMV6] KS parse errors ({Count}):\n{Detail}", diag.ErrorCount, detail);
            return detail;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WorkflowEditorVMV6] KS parse threw");
            return $"  [EX] {ex.Message}";
        }
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
                    // User values live on the BP definition nodes — mirror them into the
                    // Variable Constants panel (as overrides) BEFORE reversing, so the
                    // reverse projection only carries the KS script defaults.
                    SyncUserValuesFromBlueprint(bp);
                    RestoreTriggerFromBlueprint(bp);
                    var ir = _bpGraphLens.Reverse(bp);
                    Log.Information("[WorkflowEditorVMV6] BP→KS: bp={BpNodes} nodes, ir={Consts} consts/{Vars} vars/{Stmts} stmts",
                        bp.Nodes.Count, ir.Constants.Count, ir.GlobalVars.Count, ir.Body.Length);
                    _lastIr = ir;
                    // R1: only overwrite the KS text when the reverse projection actually
                    // differs — preserves hand-written formatting/comments on identity trips.
                    var projected = _ksTextLens.Project(ir);
                    if (!string.Equals(projected, KsSource, StringComparison.Ordinal))
                    {
                        Log.Information("[WorkflowEditorVMV6] BP→KS 反投影改写 KS: {Old} → {New} chars", KsSource.Length, projected.Length);
                        KsSource = projected;
                    }
                }
                else if (_lastIr != null)
                {
                    var projected = _ksTextLens.Project(_lastIr);
                    if (!string.Equals(projected, KsSource, StringComparison.Ordinal))
                        KsSource = projected;
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

    // ── Trigger ↔ Blueprint entry node (P3-δ) ──

    /// <summary>
    /// Applies the PluginEvent trigger configuration to a freshly projected Blueprint
    /// (v5.1 pattern): restores the persisted entry coordinates from
    /// <see cref="TriggerConfig.EntryNodeX/Y"/>, then — when the trigger type is
    /// PluginEvent with a plugin selected — swaps the synthetic EntryNode for a
    /// <see cref="PluginTriggerNode"/> carrying PluginName/TriggerName. The node keeps
    /// the Entry's Id and output-pin Id so existing connections stay valid, and the
    /// swap happens before scope analysis so the canvas renders the trigger entry.
    /// </summary>
    private void ApplyTriggerToBlueprint(Blueprint bp)
    {
        var entry = bp.Nodes.FirstOrDefault(n => n is EntryNode);
        if (entry is null) return;

        // Restore persisted entry coordinates (they live in TriggerConfig, not IR).
        if (_triggerConfig is not null && (_triggerConfig.EntryNodeX != 0 || _triggerConfig.EntryNodeY != 0))
        {
            entry.X = _triggerConfig.EntryNodeX;
            entry.Y = _triggerConfig.EntryNodeY;
        }

        if (TriggerType != "PluginEvent" || string.IsNullOrEmpty(TriggerPluginName))
            return;

        var idx = bp.Nodes.IndexOf(entry);
        var trigger = new PluginTriggerNode
        {
            Id = entry.Id,
            Name = "PluginTrigger",
            X = entry.X,
            Y = entry.Y,
            PluginName = TriggerPluginName,
            TriggerName = TriggerName ?? string.Empty,
        };
        trigger.OutputPins[0].Id = entry.OutputPins[0].Id;
        bp.Nodes[idx] = trigger;
    }

    /// <summary>
    /// Reverses the frontend entry swap before BP→KS: when the canvas root is a
    /// <see cref="PluginTriggerNode"/>, its PluginName/TriggerName (plus coordinates)
    /// feed back into the TriggerConfig and the node reverts to an EntryNode so the
    /// reverse translator walks the exec graph normally. If no trigger node is present
    /// (user deleted it on the canvas) the trigger configuration resets to Manual.
    /// </summary>
    private void RestoreTriggerFromBlueprint(Blueprint bp)
    {
        var trigger = bp.Nodes.FirstOrDefault(n => n is PluginTriggerNode) as PluginTriggerNode;
        if (trigger is not null)
        {
            TriggerType = "PluginEvent";
            TriggerPluginName = trigger.PluginName;
            TriggerName = trigger.TriggerName;
            _triggerConfig ??= new TriggerConfig();
            _triggerConfig.EntryNodeX = trigger.X;
            _triggerConfig.EntryNodeY = trigger.Y;

            var idx = bp.Nodes.IndexOf(trigger);
            var entry = new EntryNode { Id = trigger.Id, Name = "Entry", X = trigger.X, Y = trigger.Y };
            entry.OutputPins[0].Id = trigger.OutputPins[0].Id;
            bp.Nodes[idx] = entry;
        }
        else
        {
            TriggerType = "Manual";
            TriggerPluginName = null;
            TriggerName = null;
        }
    }

    private void RenderBlueprintFromKs()
    {
        try
        {
            SyncFromEditorText();
            var parseError = ValidateKsParse();
            if (parseError != null)
            {
                ConversionError = $"KS 解析错误，无法切换至 BP:\n{parseError}\n\n提示：缩进必须是 4 空格/级，禁止 Tab。";
                StatusText = "KS 解析错误";
                return;
            }
            var helpers = new List<HelperFunction>(HelperFunctions);
            var ir = _ksTextLens.Parse(KsSource, helpers);
            _lastIr = ir;

            var bp = _bpGraphLens.Project(ir);
            ApplyTriggerToBlueprint(bp);
            Log.Information("[WFEVM] RenderBlueprintFromKs: bp={Nodes} nodes, panel={Consts} entries — restoring panel user values",
                bp.Nodes.Count, VariableConstants.Count);
            // Re-apply user overrides from the Variable Constants panel onto the freshly
            // rebuilt definition nodes (KS→BP switches must not lose BP-side overrides).
            RestoreUserValuesFromPanel(bp);
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
        Log.Information("[WorkflowEditorVMV6] LoadFromIr: {Consts} constants, {Vars} vars, {Stmts} statements, {Helpers} helpers",
            ir.Constants.Count, ir.GlobalVars.Count, ir.Body.Length, ir.HelperFunctions.Length);
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

        // Restore user constant overrides from saved data (P4-γ)
        if (kcs?.VariableConstants != null)
        {
            foreach (var kvp in kcs.VariableConstants)
            {
                var existing = VariableConstants.FirstOrDefault(c => c.Name == kvp.Key);
                if (existing != null)
                    existing.UserValue = kvp.Value;
            }
        }

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
            if (_mode != EditorMode.Blueprint)
                SyncFromEditorText();
            Log.Information("[WorkflowEditorVMV6] SaveAsync: Mode={Mode}, KsSource={Length} chars", _mode, KsSource.Length);
            var helpers = new List<HelperFunction>(HelperFunctions);
            V6Workflow ir;
            if (_mode == EditorMode.Blueprint && BlueprintVM.WorkingBlueprint != null
                && BlueprintVM.WorkingBlueprint.Nodes.Count > 0)
            {
                // BP mode: reverse the edited blueprint instead of re-parsing stale KS text.
                // Mirror definition-node user values into the panel so the saved .kcs
                // VariableConstants carry the overrides.
                SyncUserValuesFromBlueprint(BlueprintVM.WorkingBlueprint);
                ir = _bpGraphLens.Reverse(BlueprintVM.WorkingBlueprint);
            }
            else
            {
                var parseError = ValidateKsParse();
                if (parseError != null)
                {
                    // Abort the save — persisting a partially-parsed IR would permanently
                    // corrupt the .kcs file (const/var/body silently dropped).
                    StatusText = $"Save aborted — KS 解析错误:\n{parseError}\n\n提示：缩进必须是 4 空格/级，禁止 Tab。";
                    Log.Warning("[WorkflowEditorVMV6] SaveAsync aborted due to parse errors");
                    return;
                }
                ir = _ksTextLens.Parse(KsSource, helpers);
            }
            _lastIr = ir;

            var irData = KitX.WorkflowV6.Serialization.WorkflowSerializer.Serialize(ir);

            var tc = _triggerConfig ?? new TriggerConfig();
            tc.TriggerType = TriggerType;
            tc.PluginName = TriggerPluginName;
            tc.TriggerName = TriggerName;

            // Persist the entry/trigger node coordinates (P3-δ): they are not part of
            // the IR annotation system — they follow the trigger envelope in TriggerConfig.
            if (BlueprintVM.WorkingBlueprint is { } wb)
            {
                var root = wb.Nodes.FirstOrDefault(n => n is EntryNode or PluginTriggerNode);
                if (root is not null)
                {
                    tc.EntryNodeX = root.X;
                    tc.EntryNodeY = root.Y;
                }
            }

            var data = new KcsFileFormat
            {
                Id = _workflowId,
                Name = WorkflowName,
                Description = WorkflowDescription,
                Author = WorkflowAuthor,
                IrData = irData,
                IrVersion = "v6",
                VariableConstants = GetUserConstantOverridesV6() is { } ov
                    ? ov.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)
                    : new Dictionary<string, object?>(),
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

    // ── Toolbar commands ──

    /// <summary>
    /// Parses the current workflow (KS or BP mode), applies constant overrides,
    /// and executes via StructuredRoslynBackend on a background thread.
    /// ExecuteAsync is synchronous internally (Roslyn compile + Invoke), so we
    /// offload it via Task.Run to avoid blocking the UI thread.
    /// </summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        if (_executionBackend is null)
        {
            ExecutionOutput = "[v6] StructuredRoslynBackend 未注入，无法执行。";
            return;
        }

        // R1: take an explicit snapshot from the editor before parsing (KS mode only).
        if (_mode != EditorMode.Blueprint)
            SyncFromEditorText();
        Log.Information("[WorkflowEditorVMV6] Run invoked: Mode={Mode}, KsSource={Length} chars", _mode, KsSource.Length);
        Log.Information("[WorkflowEditorVMV6] Run KsSource content (first 500 chars):\n{Content}",
            KsSource.Length > 500 ? KsSource[..500] : KsSource);

        // Obtain IR + lowering (KS: ParseLowering; BP: Reverse).
        V6Workflow ir;
        LoweringResult? lowering;
        try
        {
            if (_mode == EditorMode.Blueprint && BlueprintVM.WorkingBlueprint is { Nodes.Count: > 0 } bp)
            {
                // Mirror BP definition-node user values into the panel BEFORE running so
                // the override layer applies them at runtime.
                SyncUserValuesFromBlueprint(bp);
                ir = _bpGraphLens.Reverse(bp);
                lowering = null; // ScriptCompiler fallback infers PubVarTypes from ir.GlobalVars
            }
            else
            {
                var parseError = ValidateKsParse();
                if (parseError != null)
                {
                    ExecutionOutput = $"KS 解析错误，无法执行:\n{parseError}\n\n提示：缩进必须是 4 空格/级，禁止 Tab。";
                    StatusText = "Parse Error";
                    return;
                }
                var helpers = new List<HelperFunction>(HelperFunctions);
                (ir, lowering) = _ksTextLens.ParseLowering(KsSource, helpers);
            }
        }
        catch (Exception ex)
        {
            ExecutionOutput = $"解析失败: {ex.Message}";
            Log.Error(ex, "[WorkflowEditorVMV6] Run parse failed");
            return;
        }

        ir = WorkflowOverrides.ApplyConstantOverrides(ir, GetUserConstantOverridesV6());

        Log.Information("[WorkflowEditorVMV6] Run IR: {Consts} constants, {Vars} vars, {Stmts} statements, lowering={HasLowering}",
            ir.Constants.Count, ir.GlobalVars.Count, ir.Body.Length, lowering != null);

        IsExecuting = true;
        ExecutionOutput = "执行中...";
        StatusText = "Running (v6)...";

        var tokenSource = new CancellationTokenSource();
        _cancellationTokenSource = tokenSource;

        try
        {
            var result = await Task.Run(
                () => _executionBackend.ExecuteAsync(ir, lowering, tokenSource.Token),
                tokenSource.Token).ConfigureAwait(true);

            ExecutionOutput = result.IsSuccess
                ? $"执行完成\n耗时: {result.ExecutionTimeMs}ms\n输出:\n{string.Join("\n", result.Output)}"
                : $"执行错误:\n{result.ErrorMessage}";
            StatusText = result.IsSuccess ? "Done (v6)" : "Error (v6)";
        }
        catch (OperationCanceledException)
        {
            ExecutionOutput = "执行已取消。";
            StatusText = "Cancelled (v6)";
        }
        catch (Exception ex)
        {
            ExecutionOutput = $"执行异常: {ex.Message}";
            StatusText = "Error (v6)";
            Log.Error(ex, "[WorkflowEditorVMV6] Run failed");
        }
        finally
        {
            tokenSource.Dispose();
            _cancellationTokenSource = null;
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _cancellationTokenSource?.Cancel();
        Log.Information("[WorkflowEditorVMV6] Stop invoked");
    }

    [RelayCommand]
    private async Task DebugRunAsync()
    {
        if (IsDebugging)
        {
            // Toggle: stop the active debug session.
            _cancellationTokenSource?.Cancel();
            return;
        }

        if (_executionBackend is null)
        {
            ExecutionOutput = "[v6] StructuredRoslynBackend 未注入，无法调试。";
            return;
        }

        // R1: take an explicit snapshot from the editor before parsing (KS mode only).
        if (_mode != EditorMode.Blueprint)
            SyncFromEditorText();

        // Obtain IR + lowering (same as Run).
        V6Workflow ir;
        LoweringResult? lowering;
        try
        {
            if (_mode == EditorMode.Blueprint && BlueprintVM.WorkingBlueprint is { Nodes.Count: > 0 } bp)
            {
                SyncUserValuesFromBlueprint(bp);
                ir = _bpGraphLens.Reverse(bp);
                lowering = null;
            }
            else
            {
                var parseError = ValidateKsParse();
                if (parseError != null)
                {
                    ExecutionOutput = $"KS 解析错误，无法调试:\n{parseError}\n\n提示：缩进必须是 4 空格/级，禁止 Tab。";
                    StatusText = "Parse Error";
                    return;
                }
                var helpers = new List<HelperFunction>(HelperFunctions);
                (ir, lowering) = _ksTextLens.ParseLowering(KsSource, helpers);
            }
        }
        catch (Exception ex)
        {
            ExecutionOutput = $"解析失败: {ex.Message}";
            Log.Error(ex, "[WorkflowEditorVMV6] DebugRun parse failed");
            return;
        }

        ir = WorkflowOverrides.ApplyConstantOverrides(ir, GetUserConstantOverridesV6());

        // Create debugger + wire events.
        _debugController = new RealBlueprintDebugger();
        _debugController.SetSpeed(ExecutionSpeed.RealTime);
        _debugController.NodeExecuting += OnDebugNodeExecuting;
        _debugController.NodeExecuted += OnDebugNodeExecuted;
        _debugController.VariableChanged += OnDebugVariableChanged;
        SyncBreakpointsToDebugger();

        IsDebugging = true;
        IsPaused = false;
        ExecutionOutput = "调试中...";
        StatusText = "Debugging (v6)...";

        var tokenSource = new CancellationTokenSource();
        _cancellationTokenSource = tokenSource;

        try
        {
            var result = await Task.Run(
                () => _executionBackend.ExecuteAsync(ir, lowering, tokenSource.Token, _debugController),
                tokenSource.Token).ConfigureAwait(true);

            ExecutionOutput = result.IsSuccess
                ? $"调试完成\n耗时: {result.ExecutionTimeMs}ms\n输出:\n{string.Join("\n", result.Output)}"
                : $"调试错误:\n{result.ErrorMessage}";
            StatusText = result.IsSuccess ? "Debug Done (v6)" : "Error (v6)";
        }
        catch (OperationCanceledException)
        {
            ExecutionOutput = "调试已取消。";
            StatusText = "Cancelled (v6)";
        }
        catch (Exception ex)
        {
            ExecutionOutput = $"调试异常: {ex.Message}";
            StatusText = "Error (v6)";
            Log.Error(ex, "[WorkflowEditorVMV6] DebugRun failed");
        }
        finally
        {
            CleanupDebugController();
        }
    }

    // ── Debug event callbacks (P4-β) ──

    private void OnDebugNodeExecuting(string statementId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BlueprintVM.FindNodeById(statementId)?.IsExecuting = true;
            IsPaused = _debugController?.IsPaused ?? false;
        });
    }

    private void OnDebugNodeExecuted(string statementId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (BlueprintVM.FindNodeById(statementId) is { } node)
            {
                node.IsExecuting = false;
                node.ExecutionCompleted = true;
            }
            IsPaused = _debugController?.IsPaused ?? false;
        });
    }

    private void OnDebugVariableChanged(string name, object? value)
    {
        var valStr = value?.ToString() ?? "null";
        Dispatcher.UIThread.Post(() =>
        {
            if (name.StartsWith("w:"))
            {
                // Wire value tooltip: w:{nodeId} or w:{ctrlNodeId}:{pinName}
                var remainder = name[2..];
                var colonIdx = remainder.IndexOf(':');
                if (colonIdx > 0)
                {
                    var nodeId = remainder[..colonIdx];
                    var pinName = remainder[(colonIdx + 1)..];
                    BlueprintVM.FindInputConnector(nodeId, pinName)?.RuntimeValue = valStr;
                }
                else
                {
                    BlueprintVM.FindOutputConnector(remainder)?.RuntimeValue = valStr;
                }
            }
            else
            {
                // PubVar change → variable watch panel
                UpdateRuntimeVariable(name, valStr);
            }
        });
    }

    private void UpdateRuntimeVariable(string name, string value)
    {
        var existing = RuntimeVariables.FirstOrDefault(v => v.Name == name);
        if (existing != null)
        {
            existing.Value = value;
            existing.LastUpdated = DateTime.Now;
        }
        else
        {
            RuntimeVariables.Add(new RuntimeVariableItem
            {
                Name = name,
                Value = value,
                LastUpdated = DateTime.Now,
            });
        }
    }

    // ── Debug controls (P4-β) ──

    [RelayCommand]
    private void DebugPause() => _debugController?.Pause();

    [RelayCommand]
    private void DebugStep() => _debugController?.StepNext();

    [RelayCommand]
    private void DebugContinue() => _debugController?.Continue();

    [RelayCommand]
    private void ToggleBreakpoint(BlueprintNodeVMV6? node)
    {
        if (node == null) return;
        node.IsBreakpoint = !node.IsBreakpoint;
        if (_debugController != null)
        {
            if (node.IsBreakpoint) _debugController.SetBreakpoint(node.BlueprintNodeId);
            else _debugController.RemoveBreakpoint(node.BlueprintNodeId);
        }
    }

    private void SyncBreakpointsToDebugger()
    {
        if (_debugController == null) return;
        _debugController.ClearBreakpoints();
        foreach (var n in BlueprintVM.Nodes.OfType<BlueprintNodeVMV6>())
            if (n.IsBreakpoint)
                _debugController.SetBreakpoint(n.BlueprintNodeId);
    }

    private void CleanupDebugController()
    {
        if (_debugController != null)
        {
            _debugController.NodeExecuting -= OnDebugNodeExecuting;
            _debugController.NodeExecuted -= OnDebugNodeExecuted;
            _debugController.VariableChanged -= OnDebugVariableChanged;
            _debugController = null;
        }
        BlueprintVM.ClearDebugHighlights();
        BlueprintVM.ClearRuntimeValues();
        RuntimeVariables.Clear();
        IsDebugging = false;
        IsPaused = false;
    }

    [RelayCommand]
    private void ClearOutput() => ExecutionOutput = string.Empty;
}

/// <summary>
/// A runtime variable entry shown in the debug variable watch panel.
/// </summary>
public partial class RuntimeVariableItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _value;
    [ObservableProperty] private DateTime _lastUpdated;
}