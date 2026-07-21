using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowEditorViewModelV6 — UI scaffolding for the v6-grammar workflow editor.
//
// This ViewModel is the *scaffolding* for the future v6 editor frontend. It mirrors
// the surface of WorkflowEditorViewModel (the v5 editor) at the chrome level —
// metadata fields, mode toggle, output panel, status bar — so the upcoming v6
// implementation has a ready-made window to fill in. The deep BP machinery
// (BlueprintEditorViewModel, NodeFactory, SyncService, BpGraphLens, ...) is
// INTENTIONALLY NOT referenced here. Wiring those up is the work of the v6
// implementation plan, at which point:
//   • Dashboard.csproj will add a ProjectReference to KitX.WorkflowV6
//   • This VM will obtain a WorkflowV6.SyncService / BsTextLens / BpGraphLens
//   • BlueprintEditorViewModelV6 will fill in (currently an empty stub)
//
// Until then, this VM is a compile-clean shell: the BS code editor shows a static
// placeholder, and the BP canvas shows a "BP editor pending v6 implementation"
// banner. The Run/Stop buttons are bound to no-op relay commands so the toolbar
// renders correctly.
//
// The original v5 editor (WorkflowEditorWindow + WorkflowEditorViewModel +
// BlueprintEditorViewModel) is unchanged and remains the only editor reachable
// from the WorkflowPage UI. This V6 window is intentionally not wired into any
// menu yet; it can be opened from a developer console or future debug entry.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Placeholder ViewModel for the v6-grammar workflow editor. Carries only the
/// chrome-level state (metadata + mode + output + status); real BS/BP/execution
/// wiring arrives with the v6 implementation plan.
/// </summary>
internal partial class WorkflowEditorViewModelV6 : ObservableObject
{
    public enum EditorMode { BlockScript, Blueprint }

    private EditorMode _mode = EditorMode.BlockScript;
    private string _workflowName = "Untitled Workflow (v6)";
    private string _workflowDescription = string.Empty;
    private string _workflowAuthor = string.Empty;
    private bool _isDirty;
    private string _executionOutput = string.Empty;
    private bool _isExecuting;
    private string _statusText = "v6 grammar — scaffolding only";

    /// <summary>The placeholder BS source shown in the code editor.</summary>
    public string PlaceholderSource { get; } =
        "# v6 structured BlockScript scaffolding" + Environment.NewLine +
        "# The grammar is not yet implemented. This editor is a UI shell waiting" + Environment.NewLine +
        "# for the KitX.WorkflowV6 library to provide BsTextLens / BpGraphLens." + Environment.NewLine +
        Environment.NewLine +
        "Print(\"hello, v6\")";

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

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>The placeholder BP editor ViewModel. Empty stub pending implementation.</summary>
    public BlueprintEditorViewModelV6 BlueprintVM { get; } = new();

    /// <summary>Switches to BS (BlockScript) editing mode.</summary>
    [RelayCommand]
    private void SwitchToBlockScript() => Mode = EditorMode.BlockScript;

    /// <summary>Switches to BP (Blueprint) editing mode.</summary>
    [RelayCommand]
    private void SwitchToBlueprint() => Mode = EditorMode.Blueprint;

    /// <summary>No-op Run command. Bound so the toolbar button renders enabled.</summary>
    [RelayCommand]
    private void Run()
    {
        ExecutionOutput = "[v6 scaffolding] Run is not wired. The KitX.WorkflowV6 execution backend is not yet implemented.";
        Log.Information("[WorkflowEditorVMV6] Run invoked (scaffolding — no-op)");
    }

    /// <summary>No-op Stop command.</summary>
    [RelayCommand]
    private void Stop()
    {
        Log.Information("[WorkflowEditorVMV6] Stop invoked (scaffolding — no-op)");
    }

    /// <summary>No-op Debug command.</summary>
    [RelayCommand]
    private void DebugRun()
    {
        ExecutionOutput = "[v6 scaffolding] Debug is not wired. Resumability design is captured in §5.5 of the discussion notes.";
        Log.Information("[WorkflowEditorVMV6] DebugRun invoked (scaffolding — no-op)");
    }

    /// <summary>Clears the output panel.</summary>
    [RelayCommand]
    private void ClearOutput() => ExecutionOutput = string.Empty;
}
