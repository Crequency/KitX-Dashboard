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

    /// <summary>
    /// BP canvas layout persisted in the last loaded .kcs (canonical node id → position).
    /// Re-applied on every re-projection (KS→BP switch, DebugRun reload) so node
    /// positions survive round-trips. Null when the file carried no layout.
    /// </summary>
    private Dictionary<string, BlueprintLayoutEntry>? _savedLayout;

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

        // W11: BP-side edits (wiring/comments/definition values) must mark the workflow
        // dirty — otherwise closing the window silently drops every BP change.
        BlueprintVM.BlueprintEdited += () => IsDirty = true;

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
                // Debug-level: full-source logging on every keystroke is O(n) I/O per
                // edit; the tail of the document is visible in the editor itself.
                Log.Debug("[WorkflowEditorVMV6] KsSource set: {Length} chars", value?.Length ?? 0);
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
    /// Semantics owned by <see cref="DefinitionValueSynchronizer"/>.
    /// </summary>
    private Dictionary<string, string?>? GetUserConstantOverridesV6()
        => DefinitionValueSynchronizer.GetUserOverrides(VariableConstants);

    // ── BP ↔ Variable Constants panel sync (2026-08-02) ──
    //
    // BP definition nodes carry the KS-script initialiser as a READ-ONLY DefaultValue
    // and a USER value (ConstValue / VarInitialValue) that is the BP-side counterpart
    // of the KS editor's Variable Constants panel UserValue. The two are synced on
    // mode switches / save / run so overrides survive without ever rewriting the KS
    // script text (the script keeps its defaults). The semantics live in the single
    // DefinitionValueSynchronizer service (B7); the VM only wires it at the right
    // points: before Reverse (BP→panel) and after Project (panel→BP).

    /// <summary>BP→panel: copies user values from definition nodes into the panel.</summary>
    private void SyncUserValuesFromBlueprint(Blueprint bp)
        => DefinitionValueSynchronizer.SyncBlueprintToPanel(bp, VariableConstants);

    /// <summary>Panel→BP: mirrors panel UserValues onto freshly rebuilt definition nodes.</summary>
    private void RestoreUserValuesFromPanel(Blueprint bp)
        => DefinitionValueSynchronizer.RestorePanelToBlueprint(VariableConstants, bp);

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

    /// <summary>
    /// Obtains the IR (+ lowering) for the current mode — the shared "get IR" pipeline
    /// for Run / DebugRun / Save (previously three near-identical copies):
    ///   • KS mode: snapshot the editor text, parse via ParseLowering.
    ///   • BP mode: mirror definition-node user values into the panel, then reverse
    ///     the working blueprint with the editor's helper functions re-attached.
    /// <paramref name="nodeIdToCanonicalId"/> is non-null only in BP mode: the canvas
    /// node id → canonical (FNV path) id map of the reversal (see
    /// <see cref="BpGraphLens.ReverseWithNodePaths"/>).
    /// On failure returns a fully formatted error message (parse errors carry the
    /// indent hint; exceptions are prefixed); callers present it and abort.
    /// </summary>
    private (V6Workflow? Ir, LoweringResult? Lowering, string? Error, IReadOnlyDictionary<string, string>? NodeIdToCanonicalId) BuildIrForCurrentMode()
    {
        // R1: take an explicit snapshot from the editor before parsing (KS mode only).
        if (_mode != EditorMode.Blueprint)
            SyncFromEditorText();

        try
        {
            if (_mode == EditorMode.Blueprint && BlueprintVM.WorkingBlueprint is { Nodes.Count: > 0 } bp)
            {
                // Mirror BP definition-node user values into the panel BEFORE running so
                // the override layer applies them at runtime.
                SyncUserValuesFromBlueprint(bp);
                // Re-attach KS-side privileged doc comments (block doc / file-end) from
                // the pre-reversal IR (T7 K5): the BP graph does not project them, so a
                // full reversal would otherwise drop them from saved/runtime IRs.
                var (ir, idMap) = _bpGraphLens.ReverseWithNodePaths(bp, HelperFunctions, _lastIr);
                // ScriptCompiler fallback infers PubVarTypes from ir.GlobalVars.
                _lastIr = ir;
                return (ir, null, null, idMap);
            }

            var parseError = ValidateKsParse();
            if (parseError != null)
                return (null, null,
                    $"KS 解析错误：\n{parseError}\n\n提示：缩进必须是 4 空格/级，禁止 Tab。", null);
            var helpers = new List<HelperFunction>(HelperFunctions);
            // Re-attach BP-side privileged detached graphs (B1) from the pre-parse IR —
            // the KS text does not express them, so a KS-mode edit/save after a BP
            // round-trip would otherwise drop them (symmetric counterpart of the
            // doc-comment ksPrivileged re-attachment in the BP branch above).
            var (ksIr, lowering) = _ksTextLens.ParseLowering(KsSource, helpers, _lastIr);
            _lastIr = ksIr;
            return (ksIr, lowering, null, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WorkflowEditorVMV6] BuildIrForCurrentMode failed");
            return (null, null, $"解析失败: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Rebuilds the BP canvas from a Workflow IR (reverse → project → load). Used by
    /// DebugRun so canvas node IDs are the FNV-1a statement IDs the debugger emits —
    /// palette-added nodes (random IDs) otherwise never highlight. Mirrors the
    /// KS→BP switch flow (trigger entry + panel user values re-applied).
    /// </summary>
    private void ReloadCanvasFromIr(V6Workflow ir)
    {
        var bp = _bpGraphLens.Project(ir);
        ApplyTriggerToBlueprint(bp);
        ApplySavedLayout(bp);
        RestoreUserValuesFromPanel(bp);
        var scopes = _bpGraphLens.AnalyzeScopes(bp);
        BlueprintVM.LoadBlueprint(bp, scopes);
    }

    /// <summary>
    /// Overrides projected node coordinates with the persisted layout (T5). The layout
    /// keys are CANONICAL node ids — exactly the ids Project just produced — so nodes
    /// map 1:1 back to their saved canvas positions. Entry/PluginTriggerNode ride
    /// TriggerConfig instead; DetachedGraph nodes keep their snapshot coordinates
    /// (their random ids can never match a canonical key).
    /// </summary>
    private void ApplySavedLayout(Blueprint bp)
    {
        if (_savedLayout is not { Count: > 0 } layout) return;
        foreach (var node in bp.Nodes)
        {
            if (node is EntryNode or PluginTriggerNode) continue;
            if (layout.TryGetValue(node.Id, out var pos))
            {
                node.X = pos.X;
                node.Y = pos.Y;
            }
        }
    }

    [RelayCommand]
    private async Task SwitchToBlockScript()
    {
        // Auto-save before leaving the BP canvas: the dirty state includes node
        // positions (T5), and SaveAsync writes the layout envelope — without this,
        // a BP edit followed by a KS round-trip loses coordinates.
        if (_mode == EditorMode.Blueprint && IsDirty)
            await SaveAsync();

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
                    // Re-attach KS-side privileged doc comments (block doc / file-end) from
                    // the pre-reversal IR — the BP graph does not project them (T7 K5), so
                    // a full reversal would otherwise drop them on the BP round-trip.
                    var ir = _bpGraphLens.Reverse(bp, HelperFunctions, _lastIr);
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
        // Design §2.2: a failed conversion must NOT switch modes — the user stays in the
        // current view to fix the error. The developer option bypasses the guard.
        if (!string.IsNullOrEmpty(ConversionError) && !IsDeveloperOptionEnabled)
            return;
        Mode = EditorMode.BlockScript;
    }

    [RelayCommand]
    private async Task SwitchToBlueprint()
    {
        // Auto-save before leaving the KS editor (mirror of SwitchToBlockScript).
        if (_mode == EditorMode.BlockScript && IsDirty)
            await SaveAsync();

        if (_mode == EditorMode.BlockScript)
            RenderBlueprintFromKs();
        // Design §2.2: a failed KS parse must NOT switch modes — the user stays in the
        // KS editor to fix the error. The developer option bypasses the guard.
        if (!string.IsNullOrEmpty(ConversionError) && !IsDeveloperOptionEnabled)
            return;
        Mode = EditorMode.Blueprint;
    }

    /// <summary>
    /// True when the Dashboard's developer option is enabled (Settings → General).
    /// While enabled, failed KS↔BP conversions are allowed to switch modes anyway —
    /// useful for inspecting half-parsed graphs during development.
    /// </summary>
    private static bool IsDeveloperOptionEnabled
    {
        get
        {
            try
            {
                var cfg = App.GetService<KitX.Core.Contract.Configuration.IConfigService>();
                return cfg?.AppConfig?.App?.DeveloperSetting == true;
            }
            catch { return false; }
        }
    }

    // ── Trigger ↔ Blueprint entry node (P3-δ) ──

    /// <summary>
    /// Applies the PluginEvent trigger configuration to a freshly projected Blueprint
    /// (v5.1 pattern): restores the persisted entry coordinates from
    /// <see cref="TriggerConfig.EntryNodeX/Y"/>, then — when the trigger type is
    /// PluginEvent with a plugin selected — swaps the synthetic EntryNode for a
    /// <see cref="PluginTriggerNode"/> carrying PluginName/TriggerName (in place,
    /// preserving Id/pin Id via <see cref="TriggerEntrySwapper"/>), so the canvas
    /// renders the trigger entry before scope analysis.
    /// </summary>
    private void ApplyTriggerToBlueprint(Blueprint bp)
    {
        var entry = TriggerEntrySwapper.FindEntry(bp);
        if (entry is null) return;

        // Restore persisted entry coordinates (they live in TriggerConfig, not IR).
        if (_triggerConfig is not null && (_triggerConfig.EntryNodeX != 0 || _triggerConfig.EntryNodeY != 0))
        {
            entry.X = _triggerConfig.EntryNodeX;
            entry.Y = _triggerConfig.EntryNodeY;
        }

        if (TriggerType != "PluginEvent" || string.IsNullOrEmpty(TriggerPluginName))
            return;

        TriggerEntrySwapper.SwapToPlugin(bp, TriggerPluginName, TriggerName ?? string.Empty);
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
        var trigger = TriggerEntrySwapper.SwapBackToEntry(bp);
        if (trigger is not null)
        {
            TriggerType = "PluginEvent";
            TriggerPluginName = trigger.PluginName;
            TriggerName = trigger.TriggerName;
            _triggerConfig ??= new TriggerConfig();
            _triggerConfig.EntryNodeX = trigger.X;
            _triggerConfig.EntryNodeY = trigger.Y;
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
            // Re-attach BP-side privileged detached graphs (B1) from the pre-switch IR
            // (symmetric counterpart of the doc-comment re-attachment in SwitchToBlockScript):
            // the KS text does not express detached sub-graphs, so the re-parse here would
            // otherwise drop them from the canvas on a KS→BP switch after a BP round-trip.
            var ir = _ksTextLens.Parse(KsSource, helpers, _lastIr);
            _lastIr = ir;

            var bp = _bpGraphLens.Project(ir);
            ApplyTriggerToBlueprint(bp);
            ApplySavedLayout(bp);
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

        // T5: remember the persisted BP layout so any later re-projection
        // (KS→BP switch / DebugRun reload) restores node positions.
        _savedLayout = kcs?.BlueprintLayout;

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
            Log.Information("[WorkflowEditorVMV6] SaveAsync: Mode={Mode}, KsSource={Length} chars", _mode, KsSource.Length);
            var (ir, _, error, idMap) = BuildIrForCurrentMode();
            if (error is not null || ir is null)
            {
                // Abort the save — persisting a partially-parsed IR would permanently
                // corrupt the .kcs file (const/var/body silently dropped).
                StatusText = $"Save aborted — {error}";
                Log.Warning("[WorkflowEditorVMV6] SaveAsync aborted: {Error}", error);
                return;
            }
            _lastIr = ir;

            var irData = KitX.WorkflowV6.Serialization.WorkflowSerializer.Serialize(ir);

            var tc = _triggerConfig ?? new TriggerConfig();
            tc.TriggerType = TriggerType;
            tc.PluginName = TriggerPluginName;
            tc.TriggerName = TriggerName;

            // Persist the entry/trigger node coordinates (P3-δ): they are not part of
            // the IR annotation system — they follow the trigger envelope in TriggerConfig.
            Dictionary<string, BlueprintLayoutEntry>? layout = null;
            if (BlueprintVM.WorkingBlueprint is { } wb)
            {
                var root = wb.Nodes.FirstOrDefault(n => n is EntryNode or PluginTriggerNode);
                if (root is not null)
                {
                    tc.EntryNodeX = root.X;
                    tc.EntryNodeY = root.Y;
                }

                // T5: persist the BP canvas layout. Keys are CANONICAL node ids
                // (FNV-1a of the BpRenderer path, from the Reverse id map) — never the
                // random palette ids — so the load side can look them up straight after
                // Project re-projection. Entry/PluginTriggerNode ride TriggerConfig (above);
                // DetachedGraph nodes have no path → no map key → naturally excluded.
                if (_mode == EditorMode.Blueprint && idMap is { Count: > 0 })
                {
                    layout = new Dictionary<string, BlueprintLayoutEntry>();
                    foreach (var node in wb.Nodes)
                    {
                        if (node is EntryNode or PluginTriggerNode) continue;
                        if (idMap.TryGetValue(node.Id, out var canonical))
                            layout[canonical] = new BlueprintLayoutEntry { X = node.X, Y = node.Y };
                    }
                    // Keep the in-memory layout in sync: a later re-projection
                    // (KS→BP switch / DebugRun reload) must restore THESE positions,
                    // not the ones from the originally loaded file.
                    _savedLayout = layout;
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
                // KS-mode saves must NOT clobber the persisted BP layout (the layout is
                // BP-side state; KS edits don't touch it) — carry the last known table.
                BlueprintLayout = _mode == EditorMode.Blueprint ? layout : _savedLayout,
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

        // Obtain IR + lowering (shared pipeline: KS ParseLowering / BP Reverse).
        var (ir, lowering, error, _) = BuildIrForCurrentMode();
        if (error is not null || ir is null)
        {
            ExecutionOutput = error ?? "解析失败";
            StatusText = "Error (v6)";
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

        // Obtain IR + lowering (shared pipeline: KS ParseLowering / BP Reverse).
        var (ir, lowering, error, idMap) = BuildIrForCurrentMode();
        if (error is not null || ir is null)
        {
            ExecutionOutput = $"KS 解析错误，无法调试:\n{error}\n\n提示：缩进必须是 4 空格/级，禁止 Tab。";
            StatusText = "Parse Error";
            return;
        }

        ir = WorkflowOverrides.ApplyConstantOverrides(ir, GetUserConstantOverridesV6());

        // F1: palette-added BP nodes carry random (non-FNV) IDs that can never match
        // DebugCodegen's statement IDs — reload the canvas from the reversed IR so every
        // executed node's VM ID equals its statement ID and debug highlighting works for
        // BP-added nodes too. (Coordinates reset to the layout — consistent with the
        // documented non-persistence of node positions.)
        if (_mode == EditorMode.Blueprint)
        {
            // Breakpoint migration (2026-08-06): breakpoints live on VM instances, which
            // ReloadCanvasFromIr destroys wholesale — snapshot the marked node ids BEFORE
            // the reload and re-apply them AFTER it, translating canvas ids through the
            // Reverse node-id map (random palette ids → canonical FNV ids) so breakpoints
            // survive the re-projection and Continue still stops at them.
            var breakpointIds = BlueprintVM.Nodes.OfType<BlueprintNodeVMV6>()
                .Where(n => n.IsBreakpoint)
                .Select(n => n.BlueprintNodeId)
                .ToHashSet();
            var breakpointCanonicalIds = breakpointIds
                .Select(id => idMap is not null && idMap.TryGetValue(id, out var canonical) ? canonical : id)
                .ToHashSet();

            ReloadCanvasFromIr(ir);

            foreach (var n in BlueprintVM.Nodes.OfType<BlueprintNodeVMV6>())
            {
                if (breakpointCanonicalIds.Contains(n.BlueprintNodeId))
                    n.IsBreakpoint = true;
            }
        }

        // Create debugger + wire events.
        _debugController = new RealBlueprintDebugger();
        // D2-1: debug starts in step-through mode — execution pauses at the first
        // checkpoint (first statement), then Step runs one statement and pauses again,
        // Continue free-runs to the next breakpoint.
        _debugController.SetSpeed(ExecutionSpeed.StepByStep);
        _debugController.NodeExecuting += OnDebugNodeExecuting;
        _debugController.NodeExecuted += OnDebugNodeExecuted;
        _debugController.VariableChanged += OnDebugVariableChanged;
        _debugController.ExecutionPaused += OnDebugExecutionPaused;
        _debugController.ExecutionResumed += OnDebugExecutionResumed;
        SyncBreakpointsToDebugger();

        // Pre-fill the variable watch panel with every declared const/var (overrides
        // already applied) so it is never empty — consts are read-only and only vars
        // publish runtime updates via OnVarChanged, so declarations alone would leave
        // the panel blank for workflows without assignments.
        RuntimeVariables.Clear();
        foreach (var (name, c) in ir.Constants)
            RuntimeVariables.Add(new RuntimeVariableItem
            {
                Name = name,
                Value = c.InitialValueExpression ?? (c.DictInitializer is not null ? "{...}" : "null"),
                LastUpdated = DateTime.Now,
            });
        foreach (var (name, g) in ir.GlobalVars)
            RuntimeVariables.Add(new RuntimeVariableItem
            {
                Name = name,
                Value = g.InitialValueExpression ?? (g.DictInitializer is not null ? "{...}" : ""),
                LastUpdated = DateTime.Now,
            });

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

    /// <summary>
    /// Event-driven pause state (D2-3): the debugger raises this when a checkpoint
    /// begins waiting (start / step / breakpoint / manual pause). Reflects the state
    /// on the UI thread without polling in the node callbacks.
    /// </summary>
    private void OnDebugExecutionPaused()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPaused = true;
            StatusText = "调试已暂停";
        });
    }

    private void OnDebugExecutionResumed()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPaused = false;
            StatusText = "调试运行中...";
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
                    // Set the output-port value and propagate it along the wire onto
                    // the connected input ports (input tooltips mirror the upstream value).
                    BlueprintVM.SetOutputWireValue(remainder, valStr);
                }
            }
            else if (name.StartsWith("print:"))
            {
                // Live output streaming: each Print line during the debug session is
                // appended to the Output panel in real time (the completion summary
                // replaces this text when the run finishes).
                var line = name["print:".Length..];
                _liveDebugOutput = _liveDebugOutput.Length == 0
                    ? line
                    : _liveDebugOutput + "\n" + line;
                ExecutionOutput = _liveDebugOutput;
            }
            else
            {
                // PubVar change → variable watch panel
                UpdateRuntimeVariable(name, valStr);
            }
        });
    }

    /// <summary>Accumulates live Print output during a debug session (streamed to the Output panel).</summary>
    private string _liveDebugOutput = string.Empty;

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
            _debugController.ExecutionPaused -= OnDebugExecutionPaused;
            _debugController.ExecutionResumed -= OnDebugExecutionResumed;
            _debugController = null;
        }
        BlueprintVM.ClearDebugHighlights();
        BlueprintVM.ClearRuntimeValues();
        RuntimeVariables.Clear();
        _liveDebugOutput = string.Empty;
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
