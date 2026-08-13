using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using KitX.Dashboard.Services;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// ViewModel for the Bench orchestration window (the "workbench" / future NodifyM canvas —
/// the <b>design surface</b>). Shows the config of the ToolKit selected on the management
/// page (read from <see cref="UIStateService.BenchToolkit"/>) and spawns a manual run via
/// <see cref="IBenchService"/>. The node canvas is a later GUI iteration.
/// </summary>
internal class BenchViewModel : ViewModelBase
{
    private readonly IToolkitService _toolkitService;
    private readonly IBenchService _benchService;

    private bool _isDirty;

    internal BenchViewModel(IToolkitService toolkitService, IBenchService benchService)
    {
        _toolkitService = toolkitService;
        _benchService = benchService;

        ActiveToolkit = UIStateService.BenchToolkit;
        Workflows = ActiveToolkit?.Workflows ?? [];
        Triggers = ActiveToolkit?.Triggers ?? [];
        ValidationIssues = ActiveToolkit is null
            ? []
            : [.. new ConfigValidator().Validate(ActiveToolkit).Errors];
        Canvas = ActiveToolkit is null ? null : new BenchCanvasViewModel(ActiveToolkit);

        InitCommands();
        InitEvents();
    }

    /// <summary>The ToolKit this Bench window is showing.</summary>
    internal Toolkit? ActiveToolkit { get; }

    /// <summary>The read-only NodifyM projection of the config (null when no ToolKit).</summary>
    internal BenchCanvasViewModel? Canvas { get; }

    /// <summary>The ToolKit's workflows.</summary>
    internal IReadOnlyList<ToolkitWorkflow> Workflows { get; }

    /// <summary>The ToolKit's declared triggers.</summary>
    internal IReadOnlyList<Trigger> Triggers { get; }

    /// <summary>Validation diagnostics for the config (empty when valid).</summary>
    internal IReadOnlyList<string> ValidationIssues { get; }

    /// <summary>True when no ToolKit is shown (empty state).</summary>
    internal bool IsEmpty => ActiveToolkit is null;

    /// <summary>Spawns a manual run of the first Manual trigger (exercises the pipeline).</summary>
    internal ReactiveCommand<Unit, Unit>? FireFirstManualCommand { get; set; }

    /// <summary>Human-readable summary of the shown ToolKit.</summary>
    internal string TriggerSummary => ActiveToolkit is null
        ? "未选择 ToolKit"
        : $"{Triggers.Count} 个 Trigger / {Workflows.Count} 个工作流";

    public override void InitCommands()
    {
        FireFirstManualCommand = ReactiveCommand.Create(() =>
        {
            var manual = Triggers.FirstOrDefault(t => t.Type == TriggerType.Manual);
            if (manual is not null && ActiveToolkit is not null)
            {
                _benchService.Spawn(ActiveToolkit.GetId(), manual.Id, new { source = "bench-window" });
                IsDirty = true;
            }
        });
    }

    public override void InitEvents()
    {
    }

    /// <summary>Indicates the user has triggered a run (so the UI can reflect it).</summary>
    internal bool IsDirty
    {
        get => _isDirty;
        set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }
}
