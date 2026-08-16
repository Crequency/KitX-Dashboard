using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Dashboard.Services;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Models;
using KitX.ToolKit.Visualization;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// ViewModel for the Bench orchestration window (the <b>workbench</b> — the design surface).
/// Shows the config of the ToolKit selected on the management page (read from
/// <see cref="UIStateService.BenchToolkit"/>), hosts the editable canvas
/// (<see cref="BenchCanvasViewModel"/>), saves via <see cref="IToolkitService.UpdateToolkit"/>
/// (hard validation), and spawns a manual run via <see cref="IBenchService"/>.
/// </summary>
internal class BenchViewModel : ViewModelBase, IDisposable
{
    private readonly IToolkitService _toolkitService;
    private readonly IBenchService _benchService;
    private readonly IEventService _eventService;

    private bool _isDirty;
    private Trigger? _selectedManualTrigger;
    private string? _saveMessage;
    private IReadOnlyList<Trigger> _manualTriggers = [];

    public BenchViewModel(
        IToolkitService toolkitService,
        IBenchService benchService,
        IEventService eventService,
        IPluginService pluginService)
    {
        _toolkitService = toolkitService;
        _benchService = benchService;
        _eventService = eventService;

        ActiveToolkit = UIStateService.BenchToolkit;
        Workflows = ActiveToolkit?.Workflows ?? [];
        Triggers = ActiveToolkit?.Triggers ?? [];
        ManualTriggers = Triggers.Where(t => t.Type == TriggerType.Manual).ToList();
        Canvas = ActiveToolkit is null ? null : new BenchCanvasViewModel(ActiveToolkit, pluginService);
        if (Canvas is not null)
        {
            Canvas.ConfigEdited += OnCanvasEdited;
            Canvas.EditWorkflowRequested += OpenWorkflowEditor;
        }

        RefreshMountState();

        global::Serilog.Log.Information(
            $"[BenchViewModel] Built for '{ActiveToolkit?.Meta?.Name ?? "‹null›"}' — " +
            $"triggers:{Triggers.Count} workflows:{Workflows.Count} " +
            $"canvasNodes:{Canvas?.Nodes.Count ?? 0} canvasEdges:{Canvas?.Connections.Count ?? 0}");

        InitCommands();
        InitEvents();
    }

    /// <summary>The ToolKit this Bench window is showing.</summary>
    internal Toolkit? ActiveToolkit { get; }

    /// <summary>The editable NodifyM canvas over the in-memory config (null when no ToolKit).</summary>
    internal BenchCanvasViewModel? Canvas { get; }

    /// <summary>The ToolKit's workflows.</summary>
    internal IReadOnlyList<ToolkitWorkflow> Workflows { get; }

    /// <summary>The ToolKit's declared triggers.</summary>
    internal IReadOnlyList<Trigger> Triggers { get; }

    /// <summary>Manual (Spawn) triggers available for a trial run. Re-evaluated on every
    /// canvas edit so the header trial-run controls appear/refresh after adding/removing
    /// a Manual trigger.</summary>
    internal IReadOnlyList<Trigger> ManualTriggers
    {
        get => _manualTriggers;
        private set
        {
            _manualTriggers = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(HasManualTriggers));
            this.RaisePropertyChanged(nameof(FireDisabledHint));
        }
    }

    /// <summary>True when a Manual trigger exists (drives the trial-run controls).</summary>
    internal bool HasManualTriggers => ManualTriggers.Count > 0;

    /// <summary>Why the trial-run button is disabled when there is no Manual trigger.</summary>
    internal string? FireDisabledHint => HasManualTriggers
        ? null
        : TranslateTextWithSuffix("Bench", "NoManualTrigger") ?? "没有手动触发器：先在左侧面板添加「手动触发」";

    /// <summary>True when no ToolKit is shown (empty state).</summary>
    internal bool IsEmpty => ActiveToolkit is null;

    /// <summary>Spawns a manual run of the selected (or first) Manual trigger.</summary>
    internal ReactiveCommand<Unit, Unit>? FireManualCommand { get; set; }

    /// <summary>Saves the edited config through the contract (hard-validated).</summary>
    internal ReactiveCommand<Unit, Unit>? SaveCommand { get; set; }

    /// <summary>The Manual trigger to run from the trial-run drop-down.</summary>
    internal Trigger? SelectedManualTrigger
    {
        get => _selectedManualTrigger;
        set => this.RaiseAndSetIfChanged(ref _selectedManualTrigger, value);
    }

    /// <summary>Last save/validation feedback message.</summary>
    internal string? SaveMessage
    {
        get => _saveMessage;
        set => this.RaiseAndSetIfChanged(ref _saveMessage, value);
    }

    /// <summary>Human-readable summary of the shown ToolKit.</summary>
    internal string TriggerSummary => ActiveToolkit is null
        ? TranslateTextWithSuffix("Bench", "NoToolkit") ?? "未选择 ToolKit"
        : string.Format(
            TranslateTextWithSuffix("Bench", "TriggerSummary") ?? "{0} 个 Trigger / {1} 个工作流",
            Triggers.Count, Workflows.Count);

    /// <summary>Indicates the user has triggered a run (so the UI can reflect it).</summary>
    internal bool IsDirty
    {
        get => _isDirty;
        set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    /// <summary>True while the shown ToolKit is mounted (read-only banner).</summary>
    internal bool IsMounted { get; private set; }

    /// <summary>Unmounts and leaves the canvas editable (Bench UX v2 §4.6).</summary>
    internal ReactiveCommand<Unit, Unit>? UnmountAndEditCommand { get; set; }

    /// <summary>Opens the mermaid preview window (Bench UX v2 C18).</summary>
    internal ReactiveCommand<Unit, Unit>? ExportMermaidCommand { get; set; }

    /// <summary>Refresh mount state after external mount/unmount (window activation).</summary>
    internal void RefreshMountState()
    {
        IsMounted = ActiveToolkit is not null && _toolkitService.IsMounted(ActiveToolkit.GetId());
        this.RaisePropertyChanged(nameof(IsMounted));
    }

    /// <summary>Save path used by both the header button and close-confirmation.</summary>
    internal bool TrySave()
    {
        if (ActiveToolkit is null || Canvas is null)
            return false;

        if (_toolkitService.IsMounted(ActiveToolkit.GetId()))
        {
            SaveMessage = TranslateTextWithSuffix("Bench", "SaveMountedRejected") ?? "工具箱已挂载，请先卸载后再保存配置";
            Canvas.IsDiagnosticsExpanded = true;
            return false;
        }

        if (Canvas.ValidationErrors.Count > 0)
        {
            SaveMessage = string.Format(
                TranslateTextWithSuffix("Bench", "SaveInvalidRejected") ?? "配置校验失败（{0} 项），保存被拒绝",
                Canvas.ValidationErrors.Count);
            Canvas.IsDiagnosticsExpanded = true;
            return false;
        }

        try
        {
            _toolkitService.UpdateToolkit(ActiveToolkit);
            SaveMessage = TranslateTextWithSuffix("Bench", "Saved") ?? "已保存";
            IsDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            SaveMessage = string.Format(
                TranslateTextWithSuffix("Bench", "SaveFailed") ?? "保存失败：{0}", ex.Message);
            return false;
        }
    }

    /// <summary>Save button path: on rejection show the diagnostics summary popup (C15).</summary>
    private async Task SaveWithFeedbackAsync()
    {
        if (TrySave())
            return;

        var owner = ActiveToolkit is null ? null : UIStateService.BenchWindows.GetValueOrDefault(ActiveToolkit.GetId());
        var box = MessageBoxManager.GetMessageBoxStandard(
            TranslateTextWithSuffix("Bench", "SaveRejectedTitle") ?? "保存被拒绝",
            SaveMessage ?? TranslateTextWithSuffix("Bench", "SaveRejected") ?? "配置校验未通过",
            ButtonEnum.Ok,
            Icon.Error);
        if (owner is not null)
            await box.ShowWindowDialogAsync(owner);
        else
            await box.ShowWindowAsync();
    }

    public override void InitCommands()
    {
        FireManualCommand = ReactiveCommand.Create(() =>
        {
            var manual = SelectedManualTrigger ?? ManualTriggers.FirstOrDefault();
            if (manual is null || ActiveToolkit is null)
                return;

            var instanceId = _benchService.Spawn(ActiveToolkit.GetId(), manual.Id, new { source = "bench-window" });
            if (instanceId is null)
            {
                SaveMessage = TranslateTextWithSuffix("Bench", "SpawnRejected") ?? "试运行失败：工具箱未挂载或已达最大实例数";
                return;
            }

            SaveMessage = null;
            UIStateService.ShowPanelHostWindow();
            // auto = present the instance panel; silent = only select the new instance (C19).
            var openPanel = manual.Config?.Surface is null or "auto";
            (UIStateService.PanelHostWindow?.DataContext as PanelHostViewModel)
                ?.FocusInstance(instanceId, openPanel);
        });

        SaveCommand = ReactiveCommand.CreateFromTask(SaveWithFeedbackAsync);

        UnmountAndEditCommand = ReactiveCommand.Create(() =>
        {
            if (ActiveToolkit is null)
                return;
            try
            {
                _toolkitService.Unmount(ActiveToolkit.GetId());
                SaveMessage = TranslateTextWithSuffix("Bench", "Unmounted") ?? "已卸载，可以编辑配置";
            }
            catch (Exception ex)
            {
                SaveMessage = string.Format(
                    TranslateTextWithSuffix("Bench", "UnmountFailed") ?? "卸载失败：{0}", ex.Message);
            }

            RefreshMountState();
        });

        ExportMermaidCommand = ReactiveCommand.Create(() =>
        {
            if (ActiveToolkit is null)
                return;
            var text = MermaidExporter.Export(ActiveToolkit);
            var window = new Views.MermaidExportWindow(text);
            var owner = UIStateService.BenchWindows.GetValueOrDefault(ActiveToolkit.GetId());
            if (owner is not null)
                window.ShowDialog(owner);
            else
                window.Show();
        });
    }

    public override void InitEvents()
    {
        _eventService.Subscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }

    private void OnLanguageChanged(object? sender, System.EventArgs e)
        => this.RaisePropertyChanged(nameof(TriggerSummary));

    /// <summary>
    /// Unsubscribes the language handler. Called when the owning <see cref="BenchWindow"/>
    /// closes — the VM is transient per window, so a leak would accumulate one subscription
    /// per workbench open.
    /// </summary>
    public void Dispose()
    {
        _eventService.Unsubscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }

    /// <summary>Called for every canvas/config edit: dirty state + header-derived state refresh.</summary>
    private void OnCanvasEdited()
    {
        IsDirty = true;
        RefreshManualTriggers();
        this.RaisePropertyChanged(nameof(TriggerSummary));
    }

    private void RefreshManualTriggers()
    {
        ManualTriggers = Triggers.Where(t => t.Type == TriggerType.Manual).ToList();
        if (SelectedManualTrigger is not null && !ManualTriggers.Contains(SelectedManualTrigger))
            SelectedManualTrigger = ManualTriggers.FirstOrDefault();
    }

    private void OpenWorkflowEditor(ToolkitWorkflow workflow)
    {
        var toolkitId = ActiveToolkit?.GetId() ?? string.Empty;
        var filePath = Services.ToolkitWorkflowFileService.ResolveWorkflowPath(toolkitId, workflow.File);
        var key = workflow.Id;

        if (UIStateService.WorkflowEditorWindows.TryGetValue(key, out var existing) && existing is { IsVisible: true })
        {
            existing.Activate();
            return;
        }

        var window = new Views.WorkflowEditorWindowV6();
        UIStateService.WorkflowEditorWindows[key] = window;
        window.Closed += (_, _) => UIStateService.WorkflowEditorWindows.Remove(key);
        window.Show();
        _ = window.LoadWorkflowFileAsync(filePath);
    }
}
