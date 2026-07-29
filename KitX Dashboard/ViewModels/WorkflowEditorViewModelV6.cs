using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using Serilog;
using V6Workflow = KitX.WorkflowV6.Ir.Workflow;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowEditorViewModelV6 — v6 workflow editor with KS/BP mode switching.
//
// Implements the "switch-time conversion" state machine (per the v6 frontend design):
//   • KS editing is purely local (no IR sync until switch/save/compile).
//   • Switching KS→BP: KsTextLens.Parse → IR → BpGraphLens.Project → AnalyzeScopes → render.
//   • Switching BP→KS: BpGraphLens.Reverse → IR → KsTextLens.Project → text.
//   • BP editing (P3) will call StructuralReducer on every connectivity edit.
//
// P1 scope: KS editing + read-only BP rendering. Run/Debug/Save are stubs for now
// (wired in P4/P5). The window is not yet reachable from the menu — open via code.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// v6 workflow editor ViewModel. Manages KS/BP mode switching via the IR-centred
/// state machine, KS source text, and the read-only blueprint canvas.
/// </summary>
internal partial class WorkflowEditorViewModelV6 : ObservableObject
{
    /// <summary>KS (text) or BP (blueprint) view.</summary>
    public enum EditorMode { BlockScript, Blueprint }

    private readonly KsTextLens _ksTextLens;
    private readonly BpGraphLens _bpGraphLens;

    private EditorMode _mode = EditorMode.BlockScript;
    private string _workflowName = "Untitled Workflow (v6)";
    private string _workflowDescription = string.Empty;
    private string _workflowAuthor = string.Empty;
    private bool _isDirty;
    private string _executionOutput = string.Empty;
    private bool _isExecuting;
    private string _statusText = "Ready (v6)";
    private string _ksSource = string.Empty;
    private string _conversionError = string.Empty;

    /// <summary>
    /// Default KS source loaded into the editor on first open — a small sample
    /// demonstrating const/var blocks, pipeline, if/else, and forEach.
    /// </summary>
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
        _ksSource = DefaultSource;
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

    // ── Chrome properties ──

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

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>KS source text (bound to AvaloniaEdit).</summary>
    public string KsSource
    {
        get => _ksSource;
        set
        {
            if (SetProperty(ref _ksSource, value))
                IsDirty = true;
        }
    }

    /// <summary>Error message from the last KS→BP or BP→KS conversion (empty = success).</summary>
    public string ConversionError
    {
        get => _conversionError;
        set => SetProperty(ref _conversionError, value);
    }

    /// <summary>The read-only blueprint editor ViewModel.</summary>
    public BlueprintEditorViewModelV6 BlueprintVM { get; } = new();

    // ── Mode switch commands ──

    /// <summary>Switches to KS (text) mode. If coming from BP, reverse-translates BP→KS.</summary>
    [RelayCommand]
    private void SwitchToBlockScript()
    {
        if (_mode == EditorMode.Blueprint)
        {
            // BP→KS: reverse the current blueprint back to KS text.
            try
            {
                // The current blueprint is read-only (P1); we reverse from the last
                // projected IR. Since we stored the IR at switch time, re-project KS.
                // In P1 (read-only BP), the blueprint hasn't changed, so we simply
                // re-render KS from the stored IR. If no IR is stored (edge case),
                // keep the existing text.
                if (_lastIr != null)
                {
                    KsSource = _ksTextLens.Project(_lastIr);
                }
                ConversionError = string.Empty;
            }
            catch (Exception ex)
            {
                ConversionError = $"BP→KS 转换失败: {ex.Message}";
                Log.Warning(ex, "[WorkflowEditorVMV6] BP→KS conversion failed");
            }
        }
        Mode = EditorMode.BlockScript;
    }

    /// <summary>Switches to BP (blueprint) mode. Parses KS→IR→BP and renders the canvas.</summary>
    [RelayCommand]
    private void SwitchToBlueprint()
    {
        if (_mode == EditorMode.BlockScript)
        {
            RenderBlueprintFromKs();
        }
        Mode = EditorMode.Blueprint;
    }

    // ── KS → BP rendering ──

    private V6Workflow? _lastIr;

    /// <summary>
    /// Parses the current KS source into IR, projects to Blueprint, analyzes scopes,
    /// and loads everything into the canvas. Sets <see cref="ConversionError"/> on failure.
    /// </summary>
    private void RenderBlueprintFromKs()
    {
        try
        {
            var ir = _ksTextLens.Parse(KsSource, []);
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

    // ── Toolbar stubs (P4/P5) ──

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
