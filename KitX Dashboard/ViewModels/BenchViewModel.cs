using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using KitX.Core.Contract.Event;
using KitX.Dashboard.Services;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Models;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// ViewModel for the Bench orchestration window (the <b>workbench</b> — the design surface).
/// Shows the config of the ToolKit selected on the management page (read from
/// <see cref="UIStateService.BenchToolkit"/>), hosts the editable canvas
/// (<see cref="BenchCanvasViewModel"/>), saves via <see cref="IToolkitService.UpdateToolkit"/>
/// (hard validation), and spawns a manual run via <see cref="IBenchService"/>.
/// </summary>
internal class BenchViewModel : ViewModelBase
{
    private readonly IToolkitService _toolkitService;
    private readonly IBenchService _benchService;
    private readonly IEventService _eventService;

    private bool _isDirty;
    private Trigger? _selectedManualTrigger;
    private string? _saveMessage;

    public BenchViewModel(IToolkitService toolkitService, IBenchService benchService, IEventService eventService)
    {
        _toolkitService = toolkitService;
        _benchService = benchService;
        _eventService = eventService;

        ActiveToolkit = UIStateService.BenchToolkit;
        Workflows = ActiveToolkit?.Workflows ?? [];
        Triggers = ActiveToolkit?.Triggers ?? [];
        ManualTriggers = Triggers.Where(t => t.Type == TriggerType.Manual).ToList();
        Canvas = ActiveToolkit is null ? null : new BenchCanvasViewModel(ActiveToolkit);

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

    /// <summary>Manual (Spawn) triggers available for a trial run.</summary>
    internal IReadOnlyList<Trigger> ManualTriggers { get; }

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

    public override void InitCommands()
    {
        FireManualCommand = ReactiveCommand.Create(() =>
        {
            var manual = SelectedManualTrigger ?? ManualTriggers.FirstOrDefault();
            if (manual is not null && ActiveToolkit is not null)
            {
                _benchService.Spawn(ActiveToolkit.GetId(), manual.Id, new { source = "bench-window" });
                IsDirty = true;
                SaveMessage = null;
            }
        });

        SaveCommand = ReactiveCommand.Create(() =>
        {
            if (ActiveToolkit is null || Canvas is null)
                return;

            if (_toolkitService.IsMounted(ActiveToolkit.GetId()))
            {
                SaveMessage = TranslateTextWithSuffix("Bench", "SaveMountedRejected") ?? "工具箱已挂载，请先卸载后再保存配置";
                return;
            }

            if (Canvas.ValidationErrors.Count > 0)
            {
                SaveMessage = string.Format(
                    TranslateTextWithSuffix("Bench", "SaveInvalidRejected") ?? "配置校验失败（{0} 项），保存被拒绝",
                    Canvas.ValidationErrors.Count);
                return;
            }

            try
            {
                _toolkitService.UpdateToolkit(ActiveToolkit);
                SaveMessage = TranslateTextWithSuffix("Bench", "Saved") ?? "已保存";
            }
            catch (Exception ex)
            {
                SaveMessage = string.Format(
                    TranslateTextWithSuffix("Bench", "SaveFailed") ?? "保存失败：{0}", ex.Message);
            }
        });
    }

    public override void InitEvents()
    {
        _eventService.Subscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }

    private void OnLanguageChanged(object? sender, System.EventArgs e)
        => this.RaisePropertyChanged(nameof(TriggerSummary));

    private void RefreshManualTriggers()
    {
        // Re-evaluate after language change / external mutation.
    }
}
