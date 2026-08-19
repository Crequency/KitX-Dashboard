using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.Core.Contract.Plugin;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// The Bench canvas <b>editor</b> — config is the single source of truth and the canvas is
/// a live editor over an in-memory <see cref="Toolkit"/> config (GUI RFC §8.3 closed loop).
/// Edit ops (connect / disconnect / delete / palette-add / inspector) mutate the in-memory
/// config directly; saving runs a hard validation and rejects on error.
/// </summary>
public sealed partial class BenchCanvasViewModel : NodifyEditorViewModelBase
{
    /// <summary>Node discriminant.</summary>
    public enum BenchNodeKind { Source, Workflow, Panel, Comment }

    private readonly Toolkit _toolkit;
    private readonly IPluginService? _pluginService;
    private IToolkitWorkflowFileStore? _fileStore;
    private readonly Dictionary<BenchNodeVM, object> _nodeConfig = new();
    private readonly Dictionary<BenchConnectorVM, (BenchNodeVM node, string pin)> _connectorInfo = new();
    private int _sourceCount;
    private int _workflowCount;
    private bool _hasPanel;
    private bool _isCommentMode;
    private bool _isEdgeMode;
    private readonly Dictionary<UiControl, BenchUiControlVM> _panelControlPreviewVMs = new();

    // ── External-save fingerprint / rebuild short-circuit (G5) ──
    // The window re-projects the canvas on every activation (BenchWindow.axaml Activated →
    // Rebuild). For a 280-node graph that is a full clear+rebuild+position-restore+reselect
    // even when the config never changed. Two layers short-circuit it:
    //   * whole-config signature — Rebuild() skips when the canvas-relevant config content
    //     is identical to the last-built graph (covers the unguarded activation call);
    //   * per-workflow signature — ApplyWorkflowSaved skips a re-broadcast of the same
    //     external save payload (covers the WorkflowDataSaved path), gated by a local-edit
    //     guard so in-progress edits are never optimized away.
    // Both signatures are exact content strings (no hash), so an equality match is exact —
    // a false skip (stale canvas) is impossible.

    /// <summary>Fingerprint of the canvas-relevant config content at the last graph build.</summary>
    private string? _configFingerprint;

    /// <summary>Per-workflow fingerprint of the last externally-applied save (keyed by workflow id).</summary>
    private readonly Dictionary<string, string> _appliedWorkflowFingerprints = new();

    /// <summary>True when a local (canvas) edit happened since the last external workflow apply.</summary>
    private bool _hasLocalEditsSinceApply;

    /// <summary>
    /// Forces the next <see cref="Rebuild"/> to run even when the whole-config signature
    /// already matches. Set by <see cref="ApplyWorkflowSaved"/> when it applies a real change,
    /// so a revert back to a previously-built content still re-syncs a manually-written node
    /// label (config is the single source of truth).
    /// </summary>
    private bool _forceRebuildOnce;

    /// <summary>Test seam: number of graph reconstructions actually performed by <see cref="Rebuild"/>.</summary>
    internal int RebuildCount { get; private set; }

    public BenchCanvasViewModel(Toolkit toolkit, IPluginService? pluginService = null, IToolkitWorkflowFileStore? fileStore = null)
    {
        _toolkit = toolkit ?? throw new ArgumentNullException(nameof(toolkit));
        _pluginService = pluginService;
        _fileStore = fileStore;
        ToolkitInspector = new BenchToolkitInspectorVM(toolkit, pluginService, NotifyEdited);
        SelectedNodes.CollectionChanged += (_, _) =>
            SelectedNode = SelectedNodes.OfType<BenchNodeVM>().FirstOrDefault();
        BuildGraph();
        RefreshPalette();
        RefreshValidation();
    }

    /// <summary>Raised after any user edit so the host can maintain dirty state.</summary>
    public event Action? ConfigEdited;

    private void NotifyEdited()
    {
        // Any local canvas edit marks the guard that keeps an external fingerprint
        // short-circuit from swallowing a still-pending local change (G5).
        _hasLocalEditsSinceApply = true;

        // Node count drives the inspector/save-button visibility (BenchWindow.axaml
        // binds Canvas.HasContent); re-raise it after every edit so a previously empty
        // canvas reveals the inspector and the save action as soon as the first node
        // is added.
        OnPropertyChanged(nameof(HasContent));
        RefreshPanelNodePreview();
        ScheduleValidation();
        ConfigEdited?.Invoke();
    }

    /// <summary>The in-memory config the canvas edits (shared with the host for saving).</summary>
    public Toolkit Config => _toolkit;

    /// <summary>True when the config had any nodes.</summary>
    public bool HasContent => Nodes.Count > 0;

    /// <summary>ToolKit page (metadata / plugins / run params) shown when nothing is selected.</summary>
    public BenchToolkitInspectorVM ToolkitInspector { get; }

    /// <summary>
    /// Shared live workflow-id options for binding-row and add-binding combo boxes.
    /// Kept as one ObservableCollection so rows created before a workflow add/rename/delete
    /// keep showing current options.
    /// </summary>
    public ObservableCollection<string> WorkflowIdOptions { get; } = [];

    // ── Palette state ──

    [ObservableProperty]
    private string _paletteSearchText = string.Empty;

    public ObservableCollection<BenchPaletteGroupVM> PaletteGroups { get; } = [];

    partial void OnPaletteSearchTextChanged(string value) => RefreshPalette();

    // ── Inspector state ──

    [ObservableProperty]
    private BenchNodeVM? _selectedNode;

    [ObservableProperty]
    private Trigger? _selectedTrigger;

    [ObservableProperty]
    private ToolkitWorkflow? _selectedWorkflow;

    [ObservableProperty]
    private UiPanel? _selectedPanel;

    [ObservableProperty]
    private UiControl? _selectedControl;

    [ObservableProperty]
    private BenchUiControlVM? _selectedControlVM;

    [ObservableProperty]
    private ObservableCollection<BenchBindingRowVM> _selectedBindings = new();

    [ObservableProperty]
    private ObservableCollection<UiControl> _selectedControls = new();

    /// <summary>Observable control-inspector rows (supports per-control editing + reorder).</summary>
    public ObservableCollection<BenchUiControlVM> SelectedControlVMs { get; } = [];

    [ObservableProperty]
    private BenchConnectionVM? _selectedConnection;

    [ObservableProperty]
    private ToolkitComment? _selectedComment;

    [ObservableProperty]
    private ObservableCollection<BenchDiagnosticVM> _diagnostics = [];

    [ObservableProperty]
    private bool _isDiagnosticsExpanded;

    [ObservableProperty]
    private string _newWorkflowName = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _validationErrors = [];

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private string? _addWorkflowId;

    partial void OnSelectedNodeChanged(BenchNodeVM? value)
    {
        _isCommentMode = value is { IsComment: true };
        _isEdgeMode = false;
        if (value is not null && SelectedConnection is not null)
            SelectedConnection = null;
        UpdateInspector();
        OnPropertyChanged(nameof(IsSourceSelected));
        OnPropertyChanged(nameof(IsWorkflowSelected));
        OnPropertyChanged(nameof(IsPanelSelected));
        OnPropertyChanged(nameof(IsCommentSelected));
        OnPropertyChanged(nameof(IsNothingSelected));
        OnPropertyChanged(nameof(IsTimerSelected));
        OnPropertyChanged(nameof(IsPluginEventSelected));
        OnPropertyChanged(nameof(IsUIEventSelected));
        OnPropertyChanged(nameof(IsEdgeSelected));
        OnPropertyChanged(nameof(IsToolkitInspectorVisible));
        RaiseInspectorPropertyChanges();
    }

    partial void OnSelectedConnectionChanged(BenchConnectionVM? value)
    {
        _isEdgeMode = value is not null;
        _isCommentMode = false;
        if (value is not null && SelectedNodes.Count > 0)
            SelectedNodes.Clear();
        UpdateCompletionInspector();
        OnPropertyChanged(nameof(IsEdgeSelected));
        OnPropertyChanged(nameof(IsBindingEdgeSelected));
        OnPropertyChanged(nameof(IsCompletionEdgeSelected));
        OnPropertyChanged(nameof(IsToolkitInspectorVisible));
        RaiseInspectorPropertyChanges();
    }

    /// <summary>
    /// Most inspector fields are plain pass-through properties (SelectedTriggerId,
    /// SelectedTriggerPluginName, SelectedWorkflowName, ...). They only notify when the
    /// user edits them, so switching selections — or a rebuild/restore — would leave
    /// TextBoxes showing the previous/empty value. Raise every pass-through binding
    /// whenever the inspector's selected object changes so the UI re-reads the model.
    /// </summary>
    private void RaiseInspectorPropertyChanges()
    {
        OnPropertyChanged(nameof(SelectedTriggerId));
        OnPropertyChanged(nameof(SelectedTriggerSurface));
        OnPropertyChanged(nameof(SelectedTriggerTimerMode));
        OnPropertyChanged(nameof(IsCronTimerMode));
        OnPropertyChanged(nameof(IsIntervalTimerMode));
        OnPropertyChanged(nameof(SelectedTriggerIntervalMs));
        OnPropertyChanged(nameof(SelectedTriggerDueTimeMs));
        OnPropertyChanged(nameof(CronMinute));
        OnPropertyChanged(nameof(CronHour));
        OnPropertyChanged(nameof(CronDay));
        OnPropertyChanged(nameof(CronMonth));
        OnPropertyChanged(nameof(CronDow));
        OnPropertyChanged(nameof(CronPreview));
        OnPropertyChanged(nameof(SelectedTriggerPluginName));
        OnPropertyChanged(nameof(SelectedTriggerTriggerName));
        OnPropertyChanged(nameof(SelectedTriggerControl));
        OnPropertyChanged(nameof(SelectedTriggerEvent));
        OnPropertyChanged(nameof(SelectedTriggerPanel));
        OnPropertyChanged(nameof(PanelControlIds));
        OnPropertyChanged(nameof(SelectedWorkflowId));
        OnPropertyChanged(nameof(SelectedWorkflowName));
        OnPropertyChanged(nameof(SelectedWorkflowFile));
        OnPropertyChanged(nameof(SelectedPanelLayout));
        OnPropertyChanged(nameof(SelectedCommentText));
        OnPropertyChanged(nameof(CompletionInspectorTrigger));
        OnPropertyChanged(nameof(CompletionFromNode));
    }

    /// <summary>Inspector panels visibility (driven by the selected node kind).</summary>
    /// <summary>The WorkflowCompletion trigger shown by the completion-edge inspector.</summary>
    public Trigger? CompletionInspectorTrigger { get; private set; }

    public ObservableCollection<BenchBindingRowVM> CompletionInspectorBindings { get; } = [];

    /// <summary>The source node shown by the completion-edge inspector.</summary>
    public BenchNodeVM? CompletionFromNode { get; private set; }

    public bool IsSourceSelected => SelectedNode is { Kind: BenchNodeKind.Source };
    public bool IsWorkflowSelected => SelectedNode is { Kind: BenchNodeKind.Workflow };
    public bool IsPanelSelected => SelectedNode is { Kind: BenchNodeKind.Panel };
    public bool IsNothingSelected => SelectedNode is null;
    public bool IsTimerSelected => SelectedTrigger is { Type: TriggerType.Timer };
    public bool IsPluginEventSelected => SelectedTrigger is { Type: TriggerType.PluginEvent };
    public bool IsUIEventSelected => SelectedTrigger is { Type: TriggerType.UIEvent };
    public bool IsCommentSelected => SelectedNode is { Kind: BenchNodeKind.Comment };
    public bool IsEdgeSelected => SelectedConnection is not null;
    public bool IsToolkitInspectorVisible => SelectedNode is null && SelectedConnection is null;
    public bool IsBindingEdgeSelected => SelectedConnection is { Kind: BenchEdgeKind.Binding };
    public bool IsCompletionEdgeSelected => SelectedConnection is { Kind: BenchEdgeKind.Completion };
    public bool HasPanel => _toolkit.UiPanel is not null;
    public bool HasValidationErrors => ValidationErrors.Count > 0;
    public string ValidationBadgeText => HasValidationErrors
        ? string.Format(ViewModelBase.TranslateTextWithSuffix("Bench", "ValidationBadge") ?? "⚠ {0} 项", ValidationErrors.Count)
        : "✓";

    // ── Inspector write-through wrappers ──
    // The inspector binds these instead of the raw POCO fields so every edit can write
    // through to the config model AND refresh the canvas projection (node Title / KindLabel)
    // plus re-validate. Fields with no canvas projection still go through a wrapper so the
    // edit triggers RefreshValidation (config stays the single source of truth).

    /// <summary>Edits the selected trigger's <see cref="Trigger.Id"/> (write-through + node Title refresh).</summary>
    public string? SelectedTriggerId
    {
        get => SelectedTrigger?.Id;
        set
        {
            if (SelectedTrigger is null || string.IsNullOrWhiteSpace(value))
                return;
            if (value == SelectedTrigger.Id)
                return;
            if (_toolkit.Triggers.Any(t => t.Id == value))
            {
                SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorTriggerIdExists") ?? "触发器 Id 已存在");
                OnPropertyChanged(nameof(SelectedTriggerId));
                return;
            }

            SelectedTrigger.Id = value;
            // Re-key the current node so Rebuild's selection-restore (matching by
            // ConfigId) finds it under the new id, and so the rebuilt pins (src:{id})
            // stay consistent for AddBinding/RemoveBinding edge sync.
            if (SelectedNode is { Kind: BenchNodeKind.Source })
                SelectedNode.ConfigId = value;
            Rebuild();
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.Surface"/> (write-through + re-validate).</summary>
    public string? SelectedTriggerSurface
    {
        get => SelectedTrigger?.Config?.Surface;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.Surface = value;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.Cron"/> (write-through + re-validate).</summary>
    public string? SelectedTriggerCron
    {
        get => SelectedTrigger?.Config?.Cron;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.Cron = value;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.IntervalMs"/> (write-through + re-validate).</summary>
    public double? SelectedTriggerIntervalMs
    {
        get => SelectedTrigger?.Config?.IntervalMs;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.IntervalMs = value;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.OneShot"/> (write-through + re-validate).</summary>
    public bool? SelectedTriggerOneShot
    {
        get => SelectedTrigger?.Config?.OneShot;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.OneShot = value;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.PluginName"/> (write-through + source KindLabel refresh).</summary>
    public string? SelectedTriggerPluginName
    {
        get => SelectedTrigger?.Config?.PluginName;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.PluginName = value;
            RefreshSourceKindLabel();
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.TriggerName"/> (write-through + re-validate).</summary>
    public string? SelectedTriggerTriggerName
    {
        get => SelectedTrigger?.Config?.TriggerName;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.TriggerName = value;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.Control"/> (write-through + source KindLabel refresh).</summary>
    public string? SelectedTriggerControl
    {
        get => SelectedTrigger?.Config?.Control;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.Control = value;
            RefreshSourceKindLabel();
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.Event"/> (write-through + re-validate).</summary>
    public string? SelectedTriggerEvent
    {
        get => SelectedTrigger?.Config?.Event;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.Event = value;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected workflow's <see cref="ToolkitWorkflow.Id"/> with a full cascade:
    /// re-keys the node, every <see cref="TriggerBinding.Workflow"/>, and any
    /// <see cref="TriggerConfig.From"/> on WorkflowCompletion triggers, then rebuilds the graph
    /// (preserving node locations) so pins and edges stay consistent.</summary>
    public string? SelectedWorkflowId
    {
        get => SelectedWorkflow?.Id;
        set
        {
            if (SelectedWorkflow is null || string.IsNullOrWhiteSpace(value))
                return;
            if (value == SelectedWorkflow.Id)
                return;
            if (_toolkit.Workflows.Any(w => w.Id == value))
            {
                SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorWorkflowIdExists") ?? "工作流 Id 已存在");
                OnPropertyChanged(nameof(SelectedWorkflowId));
                return;
            }

            var oldId = SelectedWorkflow.Id;
            SelectedWorkflow.Id = value;
            // Re-key the current node so Rebuild's selection-restore can find it under
            // the new id (same pattern as the trigger-id rename above).
            if (SelectedNode is { Kind: BenchNodeKind.Workflow })
                SelectedNode.ConfigId = value;
            foreach (var t in _toolkit.Triggers)
            {
                foreach (var b in t.Bindings)
                    if (b.Workflow == oldId)
                        b.Workflow = value;
                if (t.Type == TriggerType.WorkflowCompletion && t.Config?.From == oldId)
                    t.Config.From = value;
            }

            Rebuild();
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected workflow's <see cref="ToolkitWorkflow.Name"/> (write-through + node Title refresh).</summary>
    public string? SelectedWorkflowName
    {
        get => SelectedWorkflow?.Name;
        set
        {
            if (SelectedWorkflow is null)
                return;
            SelectedWorkflow.Name = value ?? string.Empty;
            if (SelectedNode is { Kind: BenchNodeKind.Workflow })
                SelectedNode.Title = value ?? string.Empty;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected workflow's <see cref="ToolkitWorkflow.File"/> (write-through + re-validate).</summary>
    public string? SelectedWorkflowFile
    {
        get => SelectedWorkflow?.File;
        set
        {
            if (SelectedWorkflow is null)
                return;
            SelectedWorkflow.File = value ?? string.Empty;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected panel's <see cref="UiPanel.Layout"/> (write-through + re-validate).</summary>
    public string? SelectedPanelLayout
    {
        get => SelectedPanel?.Layout;
        set
        {
            if (SelectedPanel is null)
                return;
            SelectedPanel.Layout = value ?? string.Empty;
            LastError = null;
            NotifyEdited();
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.DueTimeMs"/>.</summary>
    public double? SelectedTriggerDueTimeMs
    {
        get => SelectedTrigger?.Config?.DueTimeMs;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.DueTimeMs = value;
            NotifyEdited();
        }
    }

    /// <summary>Timer mode: Cron / Interval / OneShot (Bench UX v2 C7).</summary>
    public int SelectedTriggerTimerMode
    {
        get
        {
            if (SelectedTrigger?.Config is null)
                return 1;
            if (!string.IsNullOrWhiteSpace(SelectedTrigger.Config.Cron))
                return 2;
            return SelectedTrigger.Config.OneShot == true ? 0 : 1;
        }
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            switch (value)
            {
                case 0: // one-shot (also covers "run at KitX launch" per RFC)
                    SelectedTrigger.Config.Cron = null;
                    SelectedTrigger.Config.OneShot = true;
                    break;
                case 2: // cron
                    SelectedTrigger.Config.OneShot = null;
                    SelectedTrigger.Config.Cron = "0 * * * *";
                    break;
                default: // periodic interval
                    SelectedTrigger.Config.OneShot = null;
                    SelectedTrigger.Config.Cron = null;
                    SelectedTrigger.Config.IntervalMs ??= 1000;
                    break;
            }

            OnPropertyChanged(nameof(SelectedTriggerTimerMode));
            OnPropertyChanged(nameof(IsCronTimerMode));
            OnPropertyChanged(nameof(IsIntervalTimerMode));
            NotifyEdited();
        }
    }

    public bool IsCronTimerMode => SelectedTriggerTimerMode == 2;
    public bool IsIntervalTimerMode => SelectedTriggerTimerMode == 1;

    public string TimerModeHint => ViewModelBase.TranslateTextWithSuffix("Bench", "TimerModeHint")
        ?? "周期 = IntervalMs 每轮触发；单次 = 仅触发一次（含 KitX 启动自运行）；Cron = 5 字段表达式";

    // Cron 5-field builder (writes through to TriggerConfig.Cron).
    public string CronMinute
    {
        get => CronField(0);
        set => SetCronField(0, value);
    }

    public string CronHour
    {
        get => CronField(1);
        set => SetCronField(1, value);
    }

    public string CronDay
    {
        get => CronField(2);
        set => SetCronField(2, value);
    }

    public string CronMonth
    {
        get => CronField(3);
        set => SetCronField(3, value);
    }

    public string CronDow
    {
        get => CronField(4);
        set => SetCronField(4, value);
    }

    public string CronPreview => string.Join(" ", CronMinute, CronHour, CronDay, CronMonth, CronDow);

    private string CronField(int index)
    {
        var parts = (SelectedTrigger?.Config?.Cron ?? "0 * * * *").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return index < parts.Length ? parts[index] : "*";
    }

    private void SetCronField(int index, string value)
    {
        if (SelectedTrigger?.Config is null)
            return;
        var parts = (SelectedTrigger.Config.Cron ?? "0 * * * *").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        while (parts.Length < 5)
            parts = [.. parts, "*"];
        parts[index] = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
        SelectedTrigger.Config.Cron = string.Join(" ", parts);
        OnPropertyChanged(nameof(CronPreview));
        OnPropertyChanged(nameof(SelectedTriggerCron));
        NotifyEdited();
    }

    /// <summary>Plugin names = requirement declarations ∪ locally installed plugins (C8).</summary>
    public IReadOnlyList<string> PluginOptions
    {
        get
        {
            var names = new List<string>();
            names.AddRange(_toolkit.Plugins.Select(p => p.Name));
            try
            {
                names.AddRange(_pluginService?.GetInstalledPlugins()
                    .Select(p => p.PluginInfo?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Cast<string>() ?? []);
            }
            catch
            {
                // Plugin service may not be initialized in headless tests.
            }

            return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Panel control ids for UIEvent control picker (C9; refreshes with panel edits).</summary>
    public IReadOnlyList<string> PanelControlIds => _toolkit.UiPanel?.Controls.Select(c => c.Id).ToList() ?? [];

    /// <summary>Comment text of the selected comment node (write-through to Toolkit.Comments).</summary>
    public string SelectedCommentText
    {
        get => SelectedComment?.Text ?? string.Empty;
        set
        {
            if (SelectedComment is null)
                return;
            if (SelectedComment.Text != value)
            {
                SelectedComment.Text = value ?? string.Empty;
                if (SelectedNode is { IsComment: true })
                    SelectedNode.CommentText = value ?? string.Empty;
                NotifyEdited();
            }
        }
    }

    /// <summary>Edits the selected trigger's <see cref="TriggerConfig.Panel"/> (reserved UIEvent field).</summary>
    public string? SelectedTriggerPanel
    {
        get => SelectedTrigger?.Config?.Panel;
        set
        {
            if (SelectedTrigger?.Config is null)
                return;
            SelectedTrigger.Config.Panel = value;
            NotifyEdited();
        }
    }

    private void RefreshSourceKindLabel()
    {
        if (SelectedNode is { Kind: BenchNodeKind.Source } && SelectedTrigger is not null)
            SelectedNode.KindLabel = SourceKindLabel(SelectedTrigger);
    }

    // ── Graph construction ──

    private void BuildGraph()
    {
        Nodes.Clear();
        Connections.Clear();
        _sourceCount = 0;
        _workflowCount = 0;
        _hasPanel = false;

        foreach (var t in _toolkit.Triggers.Where(x => x.Type != TriggerType.WorkflowCompletion))
            AddSourceNode(t, _sourceCount++);
        foreach (var wf in _toolkit.Workflows)
            AddWorkflowNode(wf, _workflowCount++);
        if (_toolkit.UiPanel is not null)
            AddPanelNode(_toolkit.UiPanel);
        var commentIndex = 0;
        foreach (var comment in _toolkit.Comments)
            AddCommentNode(comment, commentIndex++);

        foreach (var t in _toolkit.Triggers)
        {
            if (t.Type == TriggerType.WorkflowCompletion)
            {
                if (string.IsNullOrWhiteSpace(t.Config?.From))
                    continue;
                foreach (var b in t.Bindings)
                    ConnectExisting("out:" + t.Config.From, "in:" + b.Workflow, BenchEdgeKind.Completion);
            }
            else
            {
                foreach (var b in t.Bindings)
                    ConnectExisting("src:" + t.Id, "in:" + b.Workflow, BenchEdgeKind.Binding);
            }
        }

        RefreshDegreeBadges();
        RefreshWorkflowIdOptions();

        // Capture the signature of the content this graph projects so a later Rebuild can
        // short-circuit when the config is unchanged (G5). Set on every build (including the
        // constructor's BuildGraph), so the very first activation Rebuild is already skipped.
        _configFingerprint = ComputeConfigFingerprint();
    }

    /// <summary>
    /// Re-runs the config→canvas projection, preserving the <see cref="BenchNodeVM.Location"/>
    /// of every config object that is still alive (matched by <see cref="BenchNodeVM.ConfigId"/>)
    /// and restoring the previously selected node (if it still exists). Used as the fallback
    /// when the config may have been mutated externally (e.g. window Activated) or after a
    /// workflow-id cascade.
    /// </summary>
    public void Rebuild()
    {
        // G5: skip the full clear+rebuild+position-restore+reselect when the config content
        // the canvas projects is unchanged since the last build (e.g. a window activation or
        // an external save that mutated nothing). Node positions are VM state, not config, so
        // an unchanged signature also means the current layout is already correct — skipping
        // is strictly cheaper and loses nothing. ApplyWorkflowSaved sets _forceRebuildOnce when
        // it applies a real change, so a revert to a previously-built content still re-syncs a
        // manually-written node label.
        if (!_forceRebuildOnce && _configFingerprint == ComputeConfigFingerprint())
            return;

        _forceRebuildOnce = false;
        RebuildCount++;

        var locations = Nodes.OfType<BenchNodeVM>()
            .ToDictionary(n => n.ConfigId, n => n.Location);
        var selectedConfigId = SelectedNode?.ConfigId;

        SelectedNodes.Clear();
        BuildGraph();

        foreach (var node in Nodes.OfType<BenchNodeVM>())
        {
            if (locations.TryGetValue(node.ConfigId, out var loc))
                node.Location = loc;
        }

        if (selectedConfigId is not null)
        {
            var restored = Nodes.OfType<BenchNodeVM>().FirstOrDefault(n => n.ConfigId == selectedConfigId);
            if (restored is not null)
                SelectedNodes.Add(restored);
        }

        RefreshValidation();
    }

    /// <summary>
    /// Applies metadata saved by the workflow editor (the global
    /// <c>EventNames.WorkflowDataSaved</c> event) to the matching workflow in the active
    /// config, then re-projects the canvas so the node label reflects the new name.
    /// No-op when the workflow id is not part of this ToolKit (e.g. a global/standalone
    /// workflow). <see cref="Rebuild"/> preserves node locations and the current selection
    /// (matched by <see cref="BenchNodeVM.ConfigId"/>), so an in-progress edit is untouched.
    /// </summary>
    internal void ApplyWorkflowSaved(string workflowId, string? name, string? description)
    {
        var workflow = _toolkit.Workflows.FirstOrDefault(w => w.Id == workflowId);
        if (workflow is null)
            return;

        // The applied content the save would produce. Name/description are the only fields
        // the workflow editor writes back; description is not persisted on the
        // ToolkitWorkflow model, so the applied signature is the workflow's identity + name.
        var appliedName = string.IsNullOrWhiteSpace(name) ? workflow.Name : name;
        var appliedSignature = workflow.Id + "\u001e" + appliedName + "\u001e" + workflow.File;

        // G5 short-circuit: an identical external save re-broadcast (e.g. on window
        // activation) is a no-op. Local unsaved edits take priority — when one is pending the
        // incoming save must still be merged (matching the pre-existing semantics), so the
        // guard blocks the short-circuit and the apply/rebuild below runs.
        if (!_hasLocalEditsSinceApply
            && _appliedWorkflowFingerprints.TryGetValue(workflowId, out var stored)
            && stored == appliedSignature)
        {
            return;
        }

        var changed = false;
        if (!string.IsNullOrWhiteSpace(name) && workflow.Name != name)
        {
            workflow.Name = name;
            changed = true;
        }

        // Force the re-projection even if the whole-config signature happens to match a
        // previously-built graph: a manual node Title write (e.g. via SelectedWorkflowName)
        // can diverge from the config, and Rebuild re-syncs it from the config truth.
        if (changed)
        {
            _forceRebuildOnce = true;
            Rebuild();
        }

        _appliedWorkflowFingerprints[workflowId] = appliedSignature;
        _hasLocalEditsSinceApply = false;
    }

    /// <summary>
    /// Exact signature of the canvas-relevant config content (everything <see cref="BuildGraph"/>
    /// and <see cref="RefreshValidation"/> read: triggers + bindings, workflows, panel, comments).
    /// Positions are intentionally excluded (node locations are VM state, never config). The
    /// signature is compared for equality only — no hash — so two equal signatures mean the
    /// config content is byte-identical and a rebuild can be skipped without any stale-canvas risk.
    /// </summary>
    private string ComputeConfigFingerprint()
    {
        var sb = new StringBuilder();

        foreach (var t in _toolkit.Triggers)
        {
            sb.Append(t.Id).Append('\u001e').Append((int)t.Type).Append('\u001e');
            var c = t.Config;
            sb.Append(c.PluginName).Append('\u001e').Append(c.TriggerName).Append('\u001e')
              .Append(c.From).Append('\u001e').Append(c.Cron).Append('\u001e')
              .Append(c.IntervalMs.HasValue ? c.IntervalMs.Value.ToString(CultureInfo.InvariantCulture) : null).Append('\u001e')
              .Append(c.OneShot?.ToString()).Append('\u001e')
              .Append(c.DueTimeMs.HasValue ? c.DueTimeMs.Value.ToString(CultureInfo.InvariantCulture) : null).Append('\u001e')
              .Append(c.Panel).Append('\u001e').Append(c.Control).Append('\u001e')
              .Append(c.Event).Append('\u001e').Append(c.Surface).Append('\u001e');
            foreach (var b in t.Bindings)
                sb.Append(b.Workflow).Append('\u001d');
            sb.Append('\u001f');
        }

        foreach (var w in _toolkit.Workflows)
            sb.Append(w.Id).Append('\u001e').Append(w.Name).Append('\u001e').Append(w.File).Append('\u001f');

        if (_toolkit.UiPanel is { } panel)
        {
            sb.Append(panel.Layout).Append('\u001e');
            foreach (var c in panel.Controls)
                sb.Append(c.Type).Append('\u001e').Append(c.Id).Append('\u001e').Append(c.Text).Append('\u001d');
            sb.Append('\u001f');
        }

        foreach (var cm in _toolkit.Comments)
            sb.Append(cm.Id).Append('\u001e').Append(cm.Text).Append('\u001f');

        return sb.ToString();
    }

    private void AddSourceNode(Trigger trigger, int index)
    {
        var node = new BenchNodeVM(trigger.Id, BenchNodeKind.Source, SourceKindLabel(trigger), trigger.Id,
            new Point(60, 40 + index * 100));
        node.AffinityLabel = trigger.Type == TriggerType.UIEvent
            ? ViewModelBase.TranslateTextWithSuffix("Bench", "AffinityInInstance") ?? "实例内"
            : "Spawn";
        node.NodeIcon = trigger.Type switch
        {
            TriggerType.Manual => Material.Icons.MaterialIconKind.Hand,
            TriggerType.Timer => Material.Icons.MaterialIconKind.Clock,
            TriggerType.PluginEvent => Material.Icons.MaterialIconKind.Puzzle,
            TriggerType.UIEvent => Material.Icons.MaterialIconKind.ViewList,
            _ => Material.Icons.MaterialIconKind.Hand,
        };
        var outPin = new BenchConnectorVM(ViewModelBase.TranslateTextWithSuffix("Bench", "ConnectorTrigger") ?? "触发", ConnectorViewModelBase.ConnectorFlow.Output, "src:" + trigger.Id);
        node.Output.Add(outPin);
        RegisterNode(node, outPin, trigger);
    }

    private void AddWorkflowNode(ToolkitWorkflow wf, int index)
    {
        var node = new BenchNodeVM(wf.Name, BenchNodeKind.Workflow, ViewModelBase.TranslateTextWithSuffix("Bench", "NodeKindWorkflow") ?? "工作流", wf.Id,
            new Point(460, 40 + index * 100));
        var inPin = new BenchConnectorVM(ViewModelBase.TranslateTextWithSuffix("Bench", "ConnectorInput") ?? "输入", ConnectorViewModelBase.ConnectorFlow.Input, "in:" + wf.Id);
        var outPin = new BenchConnectorVM(ViewModelBase.TranslateTextWithSuffix("Bench", "ConnectorOutput") ?? "输出", ConnectorViewModelBase.ConnectorFlow.Output, "out:" + wf.Id);
        node.Input.Add(inPin);
        node.Output.Add(outPin);
        _nodeConfig[node] = wf;
        _connectorInfo[inPin] = (node, "in");
        _connectorInfo[outPin] = (node, "out");
        Nodes.Add(node);
    }

    private void AddPanelNode(UiPanel panel)
    {
        var node = new BenchNodeVM(ViewModelBase.TranslateTextWithSuffix("Bench", "NodeKindPanel") ?? "GUI 面板", BenchNodeKind.Panel,
            string.Format(ViewModelBase.TranslateTextWithSuffix("Bench", "ControlCount") ?? "控件 {0}", panel.Controls.Count), "panel",
            new Point(460, 40 + _workflowCount * 100 + 60));
        _nodeConfig[node] = panel;
        Nodes.Add(node);
        RefreshPanelNodePreview();
    }

    private void AddCommentNode(ToolkitComment comment, int index)
    {
        var node = new BenchNodeVM(comment.Text, BenchNodeKind.Comment, ViewModelBase.TranslateTextWithSuffix("Bench", "NodeKindComment") ?? "注释", comment.Id,
            new Point(240, 40 + index * 110))
        {
            CommentText = comment.Text,
        };
        _nodeConfig[node] = comment;
        Nodes.Add(node);
    }

    private void RegisterNode(BenchNodeVM node, BenchConnectorVM outPin, Trigger trigger)
    {
        _nodeConfig[node] = trigger;
        _connectorInfo[outPin] = (node, "out");
        Nodes.Add(node);
    }

    private void ConnectExisting(string srcKey, string tgtKey, BenchEdgeKind kind)
    {
        var src = AllConnectors().FirstOrDefault(c => c.Key == srcKey);
        var tgt = AllConnectors().FirstOrDefault(c => c.Key == tgtKey);
        if (src is not null && tgt is not null)
            Connections.Add(new BenchConnectionVM(this, src, tgt, kind));
    }

    private IEnumerable<BenchConnectorVM> AllConnectors()
        => Nodes.OfType<BenchNodeVM>().SelectMany(n => n.Input.Cast<BenchConnectorVM>().Concat(n.Output.Cast<BenchConnectorVM>()));

    // ── Connect (rewritten for validation + config double-write) ──

    /// <inheritdoc />
    public override void Connect(ConnectorViewModelBase source, ConnectorViewModelBase target)
    {
        if (source is not BenchConnectorVM src || target is not BenchConnectorVM tgt)
            return;
        if (!_connectorInfo.TryGetValue(src, out var si) || !_connectorInfo.TryGetValue(tgt, out var ti))
            return;
        var (srcNode, _) = si;
        var (tgtNode, _) = ti;

        // Normalise direction: output → input.
        var (outCon, inCon, outNode, inNode) = src.Flow == ConnectorViewModelBase.ConnectorFlow.Output
            ? (src, tgt, srcNode, tgtNode)
            : (tgt, src, tgtNode, srcNode);

        if (outNode.Kind == BenchNodeKind.Source)
        {
            if (inNode.Kind != BenchNodeKind.Workflow)
            {
                SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorBindingOnlyWorkflow") ?? "绑定连线只能连接到工作流节点");
                return;
            }

            var trigger = (Trigger)_nodeConfig[outNode];
            if (trigger.Bindings.Any(b => b.Workflow == inNode.ConfigId))
            {
                SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorTriggerAlreadyBound") ?? "该触发器已绑定此工作流");
                return;
            }

            trigger.Bindings.Add(new TriggerBinding { Workflow = inNode.ConfigId });
            Connections.Add(new BenchConnectionVM(this, outCon, inCon, BenchEdgeKind.Binding));
        }
        else if (outNode.Kind == BenchNodeKind.Workflow)
        {
            if (inNode.Kind != BenchNodeKind.Workflow)
            {
                SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorCompletionOnlyWorkflow") ?? "完成边只能连接到工作流节点");
                return;
            }

            if (!AddCompletionEdge(outNode.ConfigId, inNode.ConfigId, outCon, inCon))
                return;
        }
        else
        {
            SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorPanelCannotBeSource") ?? "面板节点不能作为连线源");
            return;
        }

        outCon.IsConnected = true;
        inCon.IsConnected = true;
        LastError = null;
        RefreshSelectedBindings();
        RefreshDegreeBadges();
        NotifyEdited();
    }

    /// <summary>Adds (or reuses) a workflow→workflow completion edge; rejects on cycle.</summary>
    private bool AddCompletionEdge(string fromWf, string toWf, BenchConnectorVM outCon, BenchConnectorVM inCon)
    {
        var existing = _toolkit.Triggers.FirstOrDefault(t => t.Type == TriggerType.WorkflowCompletion && t.Config?.From == fromWf);
        if (existing is not null)
        {
            if (existing.Bindings.Any(b => b.Workflow == toWf))
            {
                SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorCompletionEdgeExists") ?? "该完成边已存在");
                return false;
            }

            existing.Bindings.Add(new TriggerBinding { Workflow = toWf });
        }
        else
        {
            existing = new Trigger
            {
                Id = NewTriggerId(),
                Type = TriggerType.WorkflowCompletion,
                Config = new TriggerConfig { From = fromWf },
            };
            existing.Bindings.Add(new TriggerBinding { Workflow = toWf });
            _toolkit.Triggers.Add(existing);
        }

        var added = new BenchConnectionVM(this, outCon, inCon, BenchEdgeKind.Completion);
        Connections.Add(added);

        var result = new ConfigValidator().Validate(_toolkit);
        if (result.Errors.Any(e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase)))
        {
            // Roll back the just-added completion edge so the graph stays a strict DAG.
            var t = _toolkit.Triggers.FirstOrDefault(x => x.Type == TriggerType.WorkflowCompletion && x.Config?.From == fromWf);
            if (t is not null)
            {
                t.Bindings.RemoveAll(b => b.Workflow == toWf);
                if (t.Bindings.Count == 0)
                    _toolkit.Triggers.Remove(t);
            }

            Connections.Remove(added);
            SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorCycleDetected") ?? "检测到环：完成边不得形成循环");
            return false;
        }

        return true;
    }

    // ── Disconnect / delete (config double-write) ──

    /// <inheritdoc />
    public override void DisconnectConnector(ConnectorViewModelBase connector)
    {
        var related = Connections.OfType<BenchConnectionVM>()
            .Where(c => c.Source == connector || c.Target == connector).ToList();
        foreach (var c in related)
            RemoveConnection(c);
    }

    [RelayCommand]
    private void RemoveConnection(BenchConnectionVM? connection)
    {
        if (connection is null)
            return;
        RemoveConfigEdge(connection);
        Connections.Remove(connection);
        if (SelectedConnection == connection)
            SelectedConnection = null;
        RefreshIsConnected();
        RefreshSelectedBindings();
        RefreshDegreeBadges();
        NotifyEdited();
    }

    private void RemoveConfigEdge(BenchConnectionVM conn)
    {
        if (conn.Source is not BenchConnectorVM srcCon || conn.Target is not BenchConnectorVM tgtCon)
            return;

        if (conn.Kind == BenchEdgeKind.Binding)
        {
            if (!_connectorInfo.TryGetValue(srcCon, out var si))
                return;
            var srcTrigger = (Trigger)_nodeConfig[si.node];
            var tgtWorkflow = _connectorInfo[tgtCon].node.ConfigId;
            srcTrigger.Bindings.RemoveAll(b => b.Workflow == tgtWorkflow);
        }
        else
        {
            var fromWf = _connectorInfo[srcCon].node.ConfigId;
            var toWf = _connectorInfo[tgtCon].node.ConfigId;
            var t = _toolkit.Triggers.FirstOrDefault(x => x.Type == TriggerType.WorkflowCompletion && x.Config?.From == fromWf);
            if (t is not null)
            {
                t.Bindings.RemoveAll(b => b.Workflow == toWf);
                if (t.Bindings.Count == 0)
                    _toolkit.Triggers.Remove(t);
            }
        }
    }

    [RelayCommand]
    private void DeleteNode(BenchNodeVM? node)
    {
        if (node is null)
            return;
        var related = Connections.OfType<BenchConnectionVM>()
            .Where(c => ConnectsNode(c, node)).ToList();
        foreach (var c in related)
            RemoveConfigEdge(c);
        foreach (var c in related)
            Connections.Remove(c);

        switch (node.Kind)
        {
            case BenchNodeKind.Source:
                _toolkit.Triggers.RemoveAll(t => t.Id == node.ConfigId);
                break;
            case BenchNodeKind.Workflow:
                _toolkit.Workflows.RemoveAll(w => w.Id == node.ConfigId);
                foreach (var t in _toolkit.Triggers)
                    t.Bindings.RemoveAll(b => b.Workflow == node.ConfigId);
                _toolkit.Triggers.RemoveAll(t => t.Type == TriggerType.WorkflowCompletion && t.Config?.From == node.ConfigId);
                break;
            case BenchNodeKind.Panel:
                _toolkit.UiPanel = null;
                _hasPanel = false;
                RefreshPalette();
                break;
            case BenchNodeKind.Comment:
                _toolkit.Comments.RemoveAll(c => c.Id == node.ConfigId);
                break;
        }

        CleanupNodeMaps(node);
        Nodes.Remove(node);
        if (SelectedNode == node)
        {
            SelectedNode = null;
            SelectedConnection = null;
        }

        foreach (var edge in Connections.OfType<BenchConnectionVM>())
            edge.IsSelected = edge == SelectedConnection;
        RefreshIsConnected();
        RefreshSelectedBindings();
        RefreshDegreeBadges();
        NotifyEdited();
    }

    [RelayCommand]
    private void DeleteSelectedNodes()
    {
        var toRemove = SelectedNodes.OfType<BenchNodeVM>().ToList();
        foreach (var n in toRemove)
            DeleteNode(n);
    }

    private bool ConnectsNode(BenchConnectionVM c, BenchNodeVM n)
        => (c.Source is BenchConnectorVM s && _connectorInfo.TryGetValue(s, out var si) && si.node == n)
        || (c.Target is BenchConnectorVM t && _connectorInfo.TryGetValue(t, out var ti) && ti.node == n);

    private void CleanupNodeMaps(BenchNodeVM node)
    {
        _nodeConfig.Remove(node);
        var stale = _connectorInfo.Where(kvp => kvp.Value.node == node).Select(kvp => kvp.Key).ToList();
        foreach (var c in stale)
            _connectorInfo.Remove(c);
    }

    // ── Palette ──

    [RelayCommand]
    private void AddTrigger(string? typeStr)
    {
        var type = Enum.TryParse<TriggerType>(typeStr, out var parsed) ? parsed : TriggerType.Manual;
        var trigger = new Trigger
        {
            Id = NewTriggerId(),
            Type = type,
            Config = new TriggerConfig { Surface = "auto" },
        };
        if (type == TriggerType.Timer)
        {
            // New timers default to the periodic tri-state; give the periodic mode a
            // real interval (a null IntervalMs would be coerced to 0 by the backend
            // System.Threading.Timer, which disables periodic signaling).
            trigger.Config.IntervalMs = 1000;
        }
        _toolkit.Triggers.Add(trigger);
        AddSourceNode(trigger, _sourceCount++);
        LastError = null;
        NotifyEdited();
    }

    [RelayCommand]
    private void AddPanel()
    {
        if (_toolkit.UiPanel is not null)
        {
            SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorPanelAlreadyExists") ?? "该工具箱已有一个 GUI 面板");
            return;
        }

        _toolkit.UiPanel = new UiPanel { Layout = "stack" };
        AddPanelNode(_toolkit.UiPanel);
        _hasPanel = true;
        RefreshPalette();
        LastError = null;
        NotifyEdited();
    }

    [RelayCommand]
    private void AddControl(string? type)
    {
        if (SelectedPanel is null || string.IsNullOrWhiteSpace(type))
            return;
        var control = new UiControl { Type = type, Id = NewControlId(type) };
        SelectedPanel.Controls.Add(control);
        RefreshSelectedControls();
        RefreshPanelNodeSummary();
        SelectedControl = control;
        SelectedControlVM = SelectedControlVMs.FirstOrDefault(c => c.Model == control);
        NotifyEdited();
    }

    [RelayCommand]
    private void RemoveControl(UiControl? control)
    {
        if (SelectedPanel is null || control is null)
            return;
        SelectedPanel.Controls.Remove(control);
        RefreshSelectedControls();
        RefreshPanelNodeSummary();
        if (SelectedControl == control)
        {
            SelectedControl = null;
            SelectedControlVM = null;
        }
        NotifyEdited();
    }

    [RelayCommand]
    private void AddBinding(string? workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            return;

        // The inspector's "add binding" action serves two pages: the selected source
        // trigger and the selected completion edge (its trigger has no selected node).
        if (IsCompletionEdgeSelected && CompletionInspectorTrigger is { } completionTrigger
            && SelectedConnection is { } completionEdge)
        {
            if (completionTrigger.Bindings.Any(b => b.Workflow == workflowId))
            {
                SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorWorkflowAlreadyBound") ?? "该工作流已被此触发器绑定");
                return;
            }

            completionTrigger.Bindings.Add(new TriggerBinding { Workflow = workflowId });
            if (completionEdge.Source is BenchConnectorVM completionSrc &&
                AllConnectors().FirstOrDefault(c => c.Key == "in:" + workflowId) is { } completionTgt)
            {
                Connections.Add(new BenchConnectionVM(this, completionSrc, completionTgt, BenchEdgeKind.Completion));
            }

            UpdateCompletionInspector();
            RefreshDegreeBadges();
            NotifyEdited();
            return;
        }

        if (SelectedTrigger is null)
            return;
        if (SelectedTrigger.Bindings.Any(b => b.Workflow == workflowId))
        {
            SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorWorkflowAlreadyBound") ?? "该工作流已被此触发器绑定");
            return;
        }

        SelectedTrigger.Bindings.Add(new TriggerBinding { Workflow = workflowId });

        // Mirror the new binding as a canvas edge (source out pin → workflow in pin).
        var srcCon = AllConnectors().FirstOrDefault(c => c.Key == "src:" + SelectedTrigger.Id);
        var tgtCon = AllConnectors().FirstOrDefault(c => c.Key == "in:" + workflowId);
        if (srcCon is not null && tgtCon is not null)
            Connections.Add(new BenchConnectionVM(this, srcCon, tgtCon, BenchEdgeKind.Binding));

        RefreshSelectedBindings();
        RefreshDegreeBadges();
        NotifyEdited();
    }

    [RelayCommand]
    private void RemoveBinding(BenchBindingRowVM? row)
    {
        if (row is null)
            return;

        // A binding row may belong to the source-trigger inspector or to the
        // completion-edge inspector (the latter has no selected trigger node).
        var isCompletionRow = CompletionInspectorBindings.Contains(row);
        var trigger = isCompletionRow ? CompletionInspectorTrigger : SelectedTrigger;
        if (trigger is null)
            return;

        var oldWorkflow = row.Model.Workflow;
        trigger.Bindings.Remove(row.Model);
        if (isCompletionRow && trigger.Type == TriggerType.WorkflowCompletion && trigger.Bindings.Count == 0)
            _toolkit.Triggers.Remove(trigger);

        // Drop the matching canvas edge (source out pin → workflow in pin).
        var srcKey = trigger.Type == TriggerType.WorkflowCompletion ? "out:" + trigger.Config?.From : "src:" + trigger.Id;
        var edge = Connections.OfType<BenchConnectionVM>()
            .FirstOrDefault(c => c.Source is BenchConnectorVM s && s.Key == srcKey
                              && c.Target is BenchConnectorVM t && t.Key == "in:" + oldWorkflow);
        if (edge is not null)
        {
            if (SelectedConnection == edge)
                SelectedConnection = null;
            Connections.Remove(edge);
        }

        if (isCompletionRow)
            UpdateCompletionInspector();
        else
            RefreshSelectedBindings();
        RefreshIsConnected();
        RefreshDegreeBadges();
        NotifyEdited();
    }

    // ── Structured parameter mapping (Bench UX v2 §4.4) ──

    [RelayCommand]
    private void AddParamRow(BenchBindingRowVM? row)
    {
        row?.AddParamRow();
        NotifyEdited();
    }

    [RelayCommand]
    private void RemoveParamRow(BenchParamRowVM? param)
    {
        if (param is null)
            return;
        var row = SelectedBindings.FirstOrDefault(r => r.ParamRows.Contains(param))
                  ?? CompletionInspectorBindings.FirstOrDefault(r => r.ParamRows.Contains(param));
        row?.RemoveParamRow(param);
        NotifyEdited();
    }

    [RelayCommand]
    private void SelectControl(BenchUiControlVM? control)
    {
        if (control is null)
            return;
        SelectedControlVM = control;
        SelectedControl = control.Model;
    }

    [RelayCommand]
    private void MoveControlUp(BenchUiControlVM? control) => control?.MoveUp();

    [RelayCommand]
    private void MoveControlDown(BenchUiControlVM? control) => control?.MoveDown();

    [RelayCommand]
    private void AddSelectItem(BenchUiControlVM? control) => control?.AddSelectItem();

    [RelayCommand]
    private void RemoveSelectItem(string? item)
    {
        if (SelectedControlVM is null)
            return;
        SelectedControlVM.RemoveSelectItem(item);
        NotifyEdited();
    }

    // ── Palette dispatch / new workflow / comments ──

    /// <summary>Raised when the user asks to open the v6 editor for a workflow.</summary>
    public event Action<ToolkitWorkflow>? EditWorkflowRequested;

    [RelayCommand]
    private void RunPaletteItem(BenchPaletteItemVM? item)
    {
        switch (item?.Key)
        {
            case "Manual":
            case "Timer":
            case "PluginEvent":
            case "UIEvent":
                AddTrigger(item.Key);
                break;
            case "Workflow":
                AddWorkflow();
                break;
            case "Panel":
                AddPanel();
                break;
            case "Comment":
                AddComment();
                break;
        }
    }

    /// <summary>Adds a comment node; text persists in <see cref="Toolkit.Comments"/>, position does not.</summary>
    [RelayCommand]
    private void AddComment()
    {
        var comment = new ToolkitComment { Id = "note_" + Guid.NewGuid().ToString("N")[..8], Text = ViewModelBase.TranslateTextWithSuffix("Bench", "CommentDefaultText") ?? "双击右侧文本编辑注释" };
        _toolkit.Comments.Add(comment);
        AddCommentNode(comment, _toolkit.Comments.Count - 1);
        var node = Nodes.OfType<BenchNodeVM>().FirstOrDefault(n => n.ConfigId == comment.Id);
        if (node is not null)
        {
            SelectedNodes.Clear();
            SelectedNodes.Add(node);
        }
        LastError = null;
        NotifyEdited();
    }

    /// <summary>Creates a minimal workflow .kcs + config entry + canvas node (Bench UX v2 §4.5).</summary>
    [RelayCommand]
    private async Task AddWorkflow()
    {
        var name = (NewWorkflowName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorWorkflowNameRequired") ?? "请先输入新工作流名称");
            return;
        }

        var id = "wf_" + Guid.NewGuid().ToString("N")[..8];
        var file = "workflows/" + id + ".kcs";
        var workflow = new ToolkitWorkflow { Id = id, Name = name, File = file };
        try
        {
            if (_fileStore is null)
            {
                SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorFileServiceNotInjected") ?? "文件存储服务未注入，无法创建工作流文件");
                return;
            }
            await _fileStore.WriteMinimalWorkflowAsync(_toolkit.GetId(), workflow, _toolkit.Meta.Author);
        }
        catch (Exception ex)
        {
            SetError(string.Format(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorCreateWorkflowFile") ?? "创建工作流文件失败：{0}", ex.Message));
            return;
        }

        _toolkit.Workflows.Add(workflow);
        AddWorkflowNode(workflow, _workflowCount++);
        NewWorkflowName = string.Empty;
        RefreshPalette();
        LastError = null;
        NotifyEdited();
    }

    [RelayCommand]
    private void EditWorkflow(ToolkitWorkflow? workflow)
    {
        if (workflow is not null)
            EditWorkflowRequested?.Invoke(workflow);
    }

    // ── Edge selection ──

    [RelayCommand]
    private void SelectConnection(BenchConnectionVM? connection)
    {
        if (connection is null)
            return;

        if (connection.Kind == BenchEdgeKind.Binding)
        {
            // Design decision: selecting a binding edge jumps to its source trigger page.
            var src = connection.Source as BenchConnectorVM;
            var node = Nodes.OfType<BenchNodeVM>().FirstOrDefault(n => n.ConfigId == src?.Key.Replace("src:", ""));
            if (node is not null)
            {
                SelectedNodes.Clear();
                SelectedNodes.Add(node);
            }
            return;
        }

        foreach (var edge in Connections.OfType<BenchConnectionVM>())
            edge.IsSelected = edge == connection;
        SelectedConnection = connection;
    }

    [RelayCommand]
    private void ClearConnectionSelection()
    {
        foreach (var edge in Connections.OfType<BenchConnectionVM>())
            edge.IsSelected = false;
        SelectedConnection = null;
    }

    private void UpdateCompletionInspector()
    {
        CompletionInspectorTrigger = null;
        CompletionInspectorBindings.Clear();
        CompletionFromNode = null;

        if (SelectedConnection is not { Kind: BenchEdgeKind.Completion } completion)
            return;

        var src = completion.Source as BenchConnectorVM;
        var tgt = completion.Target as BenchConnectorVM;
        if (src is null || tgt is null)
            return;

        var from = src.Key.Replace("out:", "");
        var to = tgt.Key.Replace("in:", "");
        var trigger = _toolkit.Triggers.FirstOrDefault(t =>
            t.Type == TriggerType.WorkflowCompletion && t.Config?.From == from
            && t.Bindings.Any(b => b.Workflow == to));
        CompletionInspectorTrigger = trigger;
        CompletionFromNode = Nodes.OfType<BenchNodeVM>().FirstOrDefault(n => n.ConfigId == from);
        if (trigger is null)
            return;

        foreach (var binding in trigger.Bindings)
            CompletionInspectorBindings.Add(CreateBindingRow(binding, WorkflowIdOptions, true));
    }

    /// <summary>The fixed ten-control palette offered by the panel designer.</summary>
    public static IReadOnlyList<string> PanelControlTypes { get; } =
        ["Text", "Icon", "Button", "Input", "Number", "Select", "Switch", "Log", "Progress", "Dialog"];

    /// <summary>Spawn presentation modes (D3).</summary>
    public static IReadOnlyList<string> SurfaceOptions { get; } = ["auto", "silent"];

    /// <summary>UIEvent event names.</summary>
    public static IReadOnlyList<string> EventOptions { get; } = ["Click", "Submit", "Confirm", "Change"];

    /// <summary>Panel layout strategies.</summary>
    public static IReadOnlyList<string> LayoutOptions { get; } = ["stack", "grid"];
    /// <summary>Timer tri-state labels (single / interval / cron).</summary>
    public static IReadOnlyList<string> TimerModes => [
        ViewModelBase.TranslateTextWithSuffix("Bench", "TimerModeOnce") ?? "单次",
        ViewModelBase.TranslateTextWithSuffix("Bench", "TimerModeInterval") ?? "周期",
        "Cron",
    ];

    [ObservableProperty]
    private string _addControlType = "Text";

    // ── Palette construction / filtering ──

    private void RefreshPalette()
    {
        PaletteGroups.Clear();
        var q = (PaletteSearchText ?? string.Empty).Trim();
        var match = (string text) => text.Contains(q, StringComparison.OrdinalIgnoreCase);

        var triggers = new BenchPaletteGroupVM(ViewModelBase.TranslateTextWithSuffix("Bench", "PaletteGroupTriggers") ?? "触发器",
            new BenchPaletteItemVM("Manual", ViewModelBase.TranslateTextWithSuffix("Bench", "ManualTrigger") ?? "手动触发", "触发器", Material.Icons.MaterialIconKind.Hand),
            new BenchPaletteItemVM("Timer", ViewModelBase.TranslateTextWithSuffix("Bench", "TimerTrigger") ?? "定时", "触发器", Material.Icons.MaterialIconKind.Clock),
            new BenchPaletteItemVM("PluginEvent", ViewModelBase.TranslateTextWithSuffix("Bench", "PluginEventTrigger") ?? "插件事件", "触发器", Material.Icons.MaterialIconKind.Puzzle),
            new BenchPaletteItemVM("UIEvent", ViewModelBase.TranslateTextWithSuffix("Bench", "UIEventTrigger") ?? "UI 控件事件", "触发器", Material.Icons.MaterialIconKind.ViewList,
                HasPanel ? null : ViewModelBase.TranslateTextWithSuffix("Bench", "PaletteUIEventHint") ?? "请先添加 GUI 面板"));
        var workflows = new BenchPaletteGroupVM(ViewModelBase.TranslateTextWithSuffix("Bench", "PaletteGroupWorkflows") ?? "工作流",
            new BenchPaletteItemVM("Workflow", ViewModelBase.TranslateTextWithSuffix("Bench", "NewWorkflow") ?? "新建工作流", "工作流", Material.Icons.MaterialIconKind.Plus));
        var panel = new BenchPaletteGroupVM(ViewModelBase.TranslateTextWithSuffix("Bench", "PaletteGroupPanel") ?? "GUI 面板",
            new BenchPaletteItemVM("Panel", ViewModelBase.TranslateTextWithSuffix("Bench", "AddPanel") ?? "添加面板", "GUI 面板", Material.Icons.MaterialIconKind.ViewDashboard,
                HasPanel ? ViewModelBase.TranslateTextWithSuffix("Bench", "PalettePanelHint") ?? "至多 1 个（已添加）" : null));
        var comments = new BenchPaletteGroupVM(ViewModelBase.TranslateTextWithSuffix("Bench", "PaletteGroupComments") ?? "注释",
            new BenchPaletteItemVM("Comment", ViewModelBase.TranslateTextWithSuffix("Bench", "CommentNode") ?? "注释节点", "注释", Material.Icons.MaterialIconKind.CommentOutline));

        foreach (var group in new[] { triggers, workflows, panel, comments })
        {
            foreach (var item in group.Items)
            {
                item.IsVisible = match(group.Title) || match(item.Title) || match(item.Key);
                if (item.Key == "UIEvent")
                    item.IsEnabled = HasPanel;
                if (item.Key == "Panel")
                    item.IsEnabled = !HasPanel;
            }

            var visible = group.Items.Where(i => i.IsVisible).ToList();
            if (visible.Count == 0)
                continue;
            var filtered = new BenchPaletteGroupVM(group.Title, visible.ToArray());
            PaletteGroups.Add(filtered);
        }
    }

    // ── Diagnostics ──

    /// <summary>Raised so the view can bring the target node into view.</summary>
    public event Action<BenchNodeVM>? LocateRequested;

    [RelayCommand]
    private void ToggleDiagnostics() => IsDiagnosticsExpanded = !IsDiagnosticsExpanded;

    [RelayCommand]
    private void LocateDiagnostic(BenchDiagnosticVM? diagnostic)
    {
        if (diagnostic is null || string.IsNullOrWhiteSpace(diagnostic.NodeId))
            return;

        var nodeId = diagnostic.NodeId!;
        var node = Nodes.OfType<BenchNodeVM>().FirstOrDefault(n => n.ConfigId == nodeId);
        if (node is null && diagnostic.NodeKind == "panel")
            node = Nodes.OfType<BenchNodeVM>().FirstOrDefault(n => n.Kind == BenchNodeKind.Panel);

        if (node is null)
            return;

        SelectedNodes.Clear();
        SelectedNodes.Add(node);
        IsDiagnosticsExpanded = true;
        LocateRequested?.Invoke(node);
    }

    private static BenchDiagnosticVM ParseDiagnostic(string message)
    {
        var cycle = Regex.Match(message, @"cycle detected:\s*([^\s]+)");
        if (cycle.Success)
            return new BenchDiagnosticVM(message, cycle.Groups[1].Value, "workflow");

        var control = Regex.Match(message, @"Control '([^']+)'");
        if (control.Success)
            return new BenchDiagnosticVM(message, "panel", "panel");

        var predecessor = Regex.Match(message, @"predecessor '([^']+)'");
        if (predecessor.Success)
            return new BenchDiagnosticVM(message, predecessor.Groups[1].Value, "workflow");

        var duplicateWorkflow = Regex.Match(message, @"Duplicate workflow Id '([^']+)'");
        if (duplicateWorkflow.Success)
            return new BenchDiagnosticVM(message, duplicateWorkflow.Groups[1].Value, "workflow");

        var workflow = Regex.Match(message, @"workflow '([^']+)'");
        if (workflow.Success)
            return new BenchDiagnosticVM(message, workflow.Groups[1].Value, "workflow");

        var duplicateTrigger = Regex.Match(message, @"Duplicate trigger Id '([^']+)'");
        if (duplicateTrigger.Success)
            return new BenchDiagnosticVM(message, duplicateTrigger.Groups[1].Value, "source");

        var trigger = Regex.Match(message, @"Trigger '([^']+)'");
        if (trigger.Success)
            return new BenchDiagnosticVM(message, trigger.Groups[1].Value, "source");

        return new BenchDiagnosticVM(message, null, null);
    }

    // ── Inspector helpers ──

    private void UpdateInspector()
    {
        SelectedTrigger = null;
        SelectedWorkflow = null;
        SelectedPanel = null;
        SelectedControl = null;
        SelectedControlVM = null;
        SelectedComment = null;
        SelectedBindings.Clear();
        SelectedControls.Clear();

        if (SelectedNode is null || !_nodeConfig.TryGetValue(SelectedNode, out var cfg))
            return;

        OnPropertyChanged(nameof(WorkflowIdOptions));
        OnPropertyChanged(nameof(PluginOptions));
        OnPropertyChanged(nameof(PanelControlIds));

        switch (SelectedNode.Kind)
        {
            case BenchNodeKind.Source:
                SelectedTrigger = cfg as Trigger;
                OnPropertyChanged(nameof(SelectedTriggerTimerMode));
                OnPropertyChanged(nameof(IsCronTimerMode));
                OnPropertyChanged(nameof(IsIntervalTimerMode));
                OnPropertyChanged(nameof(CronPreview));
                RefreshSelectedBindings();
                break;
            case BenchNodeKind.Workflow:
                SelectedWorkflow = cfg as ToolkitWorkflow;
                break;
            case BenchNodeKind.Panel:
                SelectedPanel = cfg as UiPanel;
                RefreshSelectedControls();
                break;
            case BenchNodeKind.Comment:
                SelectedComment = cfg as ToolkitComment;
                if (SelectedNode is { IsComment: true })
                    SelectedNode.CommentText = SelectedComment?.Text ?? string.Empty;
                break;
        }
    }

    private void RefreshSelectedBindings()
    {
        SelectedBindings = new ObservableCollection<BenchBindingRowVM>(
            (SelectedTrigger?.Bindings ?? [])
                .Select(b => CreateBindingRow(b, WorkflowIdOptions, false)));
    }

    /// <summary>Re-syncs the shared workflow-id options with the config (add/rename/delete).</summary>
    private void RefreshWorkflowIdOptions()
    {
        WorkflowIdOptions.Clear();
        foreach (var workflow in _toolkit.Workflows)
            WorkflowIdOptions.Add(workflow.Id);
    }

    /// <summary>Creates a binding row and wires the canvas edge-sync on workflow change.</summary>
    private BenchBindingRowVM CreateBindingRow(TriggerBinding model, ObservableCollection<string> options, bool completionBinding)
    {
        var row = new BenchBindingRowVM(model, options, completionBinding, NotifyEdited);
        row.WorkflowChanged += (oldWorkflow, newWorkflow) => SyncBindingWorkflow(row, oldWorkflow, newWorkflow);
        return row;
    }

    /// <summary>
    /// Keeps the canvas projection consistent when a binding row's target workflow is
    /// changed in the inspector: the old edge is replaced by the new one (config stays
    /// the single source of truth).
    /// </summary>
    private void SyncBindingWorkflow(BenchBindingRowVM row, string oldWorkflow, string newWorkflow)
    {
        var trigger = _toolkit.Triggers.FirstOrDefault(t => t.Bindings.Contains(row.Model));
        if (trigger is null)
            return;

        if (trigger.Bindings.Any(b => !ReferenceEquals(b, row.Model) && b.Workflow == newWorkflow))
        {
            row.Model.Workflow = oldWorkflow;
            row.NotifyWorkflowChanged();
            SetError(ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorWorkflowAlreadyBound") ?? "该工作流已被此触发器绑定");
            return;
        }

        var edgeKind = trigger.Type == TriggerType.WorkflowCompletion ? BenchEdgeKind.Completion : BenchEdgeKind.Binding;
        var srcKey = trigger.Type == TriggerType.WorkflowCompletion ? "out:" + trigger.Config?.From : "src:" + trigger.Id;

        var oldEdge = Connections.OfType<BenchConnectionVM>().FirstOrDefault(c =>
            c.Source is BenchConnectorVM s && s.Key == srcKey
            && c.Target is BenchConnectorVM t && t.Key == "in:" + oldWorkflow);
        if (oldEdge is not null)
            Connections.Remove(oldEdge);

        var srcCon = AllConnectors().FirstOrDefault(c => c.Key == srcKey);
        var tgtCon = AllConnectors().FirstOrDefault(c => c.Key == "in:" + newWorkflow);
        if (srcCon is not null && tgtCon is not null)
            Connections.Add(new BenchConnectionVM(this, srcCon, tgtCon, edgeKind));

        RefreshIsConnected();
        RefreshDegreeBadges();
        NotifyEdited();
    }

    private void RefreshSelectedControls()
    {
        SelectedControls.Clear();
        SelectedControlVMs.Clear();
        if (SelectedPanel is not null)
        {
            foreach (var c in SelectedPanel.Controls)
            {
                SelectedControls.Add(c);
                SelectedControlVMs.Add(new BenchUiControlVM(c, NotifyEdited, MoveControl));
            }
        }

        OnPropertyChanged(nameof(PanelControlIds));
    }

    /// <summary>Moves a control in the panel declaration (delta -1 up / +1 down).</summary>
    private void MoveControl(BenchUiControlVM control, int delta)
    {
        if (SelectedPanel is null)
            return;
        var index = SelectedPanel.Controls.IndexOf(control.Model);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= SelectedPanel.Controls.Count)
            return;
        SelectedPanel.Controls.RemoveAt(index);
        SelectedPanel.Controls.Insert(target, control.Model);
        RefreshSelectedControls();
        RefreshPanelNodeSummary();
        NotifyEdited();
    }

    /// <summary>
    /// Keeps the panel node's on-canvas control preview in sync with the config. The
    /// preview rows are observable <see cref="BenchUiControlVM"/>s (not raw POCOs), so
    /// editing Id/Text/Options in the inspector re-renders the node immediately.
    /// </summary>
    private void RefreshPanelNodePreview()
    {
        var panelNode = Nodes.OfType<BenchNodeVM>().FirstOrDefault(n => n.Kind == BenchNodeKind.Panel);
        if (panelNode is null)
            return;

        panelNode.ControlVMs.Clear();
        foreach (var control in _toolkit.UiPanel?.Controls ?? [])
        {
            if (!_panelControlPreviewVMs.TryGetValue(control, out var preview))
            {
                preview = new BenchUiControlVM(control, () => { }, (_, _) => { });
                _panelControlPreviewVMs[control] = preview;
            }

            preview.RefreshFromModel();
            panelNode.ControlVMs.Add(preview);
        }
    }

    /// <summary>Keeps the canvas panel node's "控件 N" subtitle in sync with the config.</summary>
    private void RefreshPanelNodeSummary()
    {
        var panelNode = Nodes.OfType<BenchNodeVM>().FirstOrDefault(n => n.Kind == BenchNodeKind.Panel);
        if (panelNode is not null && SelectedPanel is not null)
            panelNode.KindLabel = string.Format(ViewModelBase.TranslateTextWithSuffix("Bench", "ControlCount") ?? "控件 {0}", SelectedPanel.Controls.Count);
    }

    /// <summary>fan-out / AND-join degree badges (only shown when degree > 1).</summary>
    private void RefreshDegreeBadges()
    {
        var completionEdges = _toolkit.Triggers
            .Where(t => t.Type == TriggerType.WorkflowCompletion && !string.IsNullOrWhiteSpace(t.Config?.From))
            .SelectMany(t => t.Bindings.Select(b => (From: t.Config!.From!, To: b.Workflow)))
            .ToList();

        foreach (var node in Nodes.OfType<BenchNodeVM>().Where(n => n.Kind == BenchNodeKind.Workflow))
        {
            var outgoing = completionEdges.Count(e => e.From == node.ConfigId);
            var incoming = completionEdges.Count(e => e.To == node.ConfigId);
            node.OutDegreeText = outgoing > 1 ? $"fan-out {outgoing}" : string.Empty;
            node.InDegreeText = incoming > 1 ? $"AND-join {incoming}" : string.Empty;
        }
    }

    private void RefreshIsConnected()
    {
        // Walk every pin, not just pins still on remaining edges, so a pin whose last
        // edge was removed drops its "connected" highlight.
        foreach (var connector in AllConnectors())
            connector.IsConnected = Connections.Any(c => c.Source == connector || c.Target == connector);
    }

    // ── Validation (debounced from edits; flushed synchronously on save/close) ──

    /// <summary>Trailing window that coalesces the redundant validation triggered by rapid edits.</summary>
    private const int ValidationDebounceMs = 300;

    private CancellationTokenSource? _validationCts;

    /// <summary>Test seam: number of times the full validation actually ran.</summary>
    internal int ValidationRunCount { get; private set; }

    /// <summary>
    /// Debounced entry point for edits (see <see cref="NotifyEdited"/>). Coalesces the
    /// validate-per-keystroke burst into a single trailing execution. A pending run is never
    /// lost: the last edit always schedules a run, and <see cref="FlushValidation"/> executes
    /// it immediately (used by save / window-close paths so the validation gate is never stale).
    /// The deferred run is marshalled back through the <see cref="SynchronizationContext"/> of
    /// the thread that scheduled it (the UI thread in the running app) so observable state is
    /// still mutated on the UI thread; headless tests have no sync context, so the continuation
    /// only ever runs if the test actually waits the debounce window.
    /// </summary>
    private void ScheduleValidation()
    {
        _validationCts?.Cancel();
        _validationCts = new CancellationTokenSource();
        var token = _validationCts.Token;
        var sync = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ValidationDebounceMs, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
                return;

            if (sync is null)
                RefreshValidation();
            else
                sync.Post(_ => RefreshValidation(), null);
        });
    }

    /// <summary>Runs any pending validation immediately (the trailing debounce's final state).</summary>
    internal void FlushValidation()
    {
        _validationCts?.Cancel();
        _validationCts = null;
        RefreshValidation();
    }

    private void RefreshValidation()
    {
        ValidationRunCount++;
        var errors = new ConfigValidator().Validate(_toolkit).Errors;
        ValidationErrors = errors;
        Diagnostics = new ObservableCollection<BenchDiagnosticVM>(errors.Select(ParseDiagnostic));

        var flaggedIds = Diagnostics.Where(d => d.NodeId is not null)
            .Select(d => d.NodeId!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var node in Nodes.OfType<BenchNodeVM>())
            node.HasDiagnostic = flaggedIds.Contains(node.ConfigId);

        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(ValidationBadgeText));
    }

    private void SetError(string message)
        => LastError = message;

    // ── Misc ──

    private static string SourceKindLabel(Trigger trigger) => trigger.Type switch
    {
        TriggerType.Manual => ViewModelBase.TranslateTextWithSuffix("Bench", "TriggerKindManual") ?? "手动",
        TriggerType.PluginEvent => string.Format(ViewModelBase.TranslateTextWithSuffix("Bench", "TriggerKindPlugin") ?? "插件: {0}", trigger.Config?.PluginName),
        TriggerType.UIEvent => string.Format(ViewModelBase.TranslateTextWithSuffix("Bench", "TriggerKindUI") ?? "UI: {0}", trigger.Config?.Control),
        TriggerType.Timer => ViewModelBase.TranslateTextWithSuffix("Bench", "TimerTrigger") ?? "定时",
        _ => trigger.Type.ToString(),
    };

    private static string NewTriggerId()
        => "trg_" + Guid.NewGuid().ToString("N")[..8];

    private static string NewControlId(string type)
        => type.ToLowerInvariant() + "_" + Guid.NewGuid().ToString("N")[..6];
}
