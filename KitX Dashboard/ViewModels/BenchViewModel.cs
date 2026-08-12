using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using KitX.Dashboard.Services;
using KitX.ToolKit.Models;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.Validation;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// ViewModel for the Bench orchestration window (the "workbench" / future NodifyM canvas).
/// Scaffolding only ("not officially released"): it displays the activated ToolKit's config
/// (workflows + triggers + validation) and exposes a manual-fire command to exercise the
/// unified Trigger pipeline. The actual node canvas is a later GUI iteration.
/// </summary>
internal class BenchViewModel : ViewModelBase
{
    private readonly BenchTriggerManager _benchManager;

    private bool _isDirty;

    /// <summary>The ToolKit currently active in the <see cref="BenchTriggerManager"/>.</summary>
    internal Toolkit? ActiveToolkit { get; }

    /// <summary>The active ToolKit's workflows.</summary>
    internal IReadOnlyList<ToolkitWorkflow> Workflows { get; }

    /// <summary>The active ToolKit's declared triggers.</summary>
    internal IReadOnlyList<Trigger> Triggers { get; }

    /// <summary>Validation diagnostics for the active config (empty when valid).</summary>
    internal IReadOnlyList<string> ValidationIssues { get; }

    /// <summary>True when no ToolKit is active (empty state).</summary>
    internal bool IsEmpty => ActiveToolkit is null;

    /// <summary>Fires the first Manual trigger on the active ToolKit (exercises the pipeline).</summary>
    internal ReactiveCommand<Unit, Unit>? FireFirstManualCommand { get; set; }

    /// <summary>Human-readable summary of the active ToolKit's trigger count.</summary>
    internal string TriggerSummary => ActiveToolkit is null
        ? "未激活 ToolKit"
        : $"{Triggers.Count} 个 Trigger / {Workflows.Count} 个工作流";

    public BenchViewModel(BenchTriggerManager benchManager)
    {
        _benchManager = benchManager;

        ActiveToolkit = benchManager.ActiveToolkit;
        Workflows = ActiveToolkit?.Workflows ?? [];
        Triggers = ActiveToolkit?.Triggers ?? [];
        ValidationIssues = ActiveToolkit is null
            ? []
            : [.. new ConfigValidator().Validate(ActiveToolkit).Errors];

        InitCommands();
        InitEvents();
    }

    public override void InitCommands()
    {
        FireFirstManualCommand = ReactiveCommand.Create(() =>
        {
            var manual = Triggers.FirstOrDefault(t => t.Type == TriggerType.Manual);
            if (manual is not null)
            {
                _benchManager.Fire(manual.Id, new { source = "bench-window" });
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
