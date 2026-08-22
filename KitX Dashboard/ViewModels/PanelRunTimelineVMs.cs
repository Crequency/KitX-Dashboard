using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using KitX.ToolKit.Contracts.Events;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>One workflow chain row in the Panel host's run tab.</summary>
public sealed class RunChainVM : ReactiveObject
{
    private string _status = "running";

    public RunChainVM(string runId, string workflowId)
    {
        RunId = runId;
        WorkflowId = workflowId;
    }

    public string RunId { get; }
    public string WorkflowId { get; }

    public string Status
    {
        get => _status switch
        {
            "running" => ViewModelBase.TranslateTextWithSuffix("PanelHost", "StatusRunning") ?? "运行中",
            "completed" => ViewModelBase.TranslateTextWithSuffix("PanelHost", "StatusCompleted") ?? "已完成",
            _ => ViewModelBase.TranslateTextWithSuffix("PanelHost", "StatusFailed") ?? "失败",
        };
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public bool IsActive => _status == "running";

    public bool IsFailed { get; private set; }

    public string? Error { get; private set; }

    public void Complete(bool succeeded, string? error)
    {
        IsFailed = !succeeded;
        Error = error;
        Status = succeeded ? "completed" : "failed";
        this.RaisePropertyChanged(nameof(IsActive));
        this.RaisePropertyChanged(nameof(IsFailed));
        this.RaisePropertyChanged(nameof(Error));
    }
}

/// <summary>Per-instance accumulation of RunStarted/RunCompleted events (Bench UX v2 C24).</summary>
public sealed class InstanceRunTimelineVM
{
    public ObservableCollection<RunChainVM> ActiveRuns { get; } = [];
    public ObservableCollection<RunChainVM> CompletedRuns { get; } = [];

    public void Apply(RunStartedEvent e)
    {
        if (ActiveRuns.Any(r => r.RunId == e.RunId))
            return;
        ActiveRuns.Add(new RunChainVM(e.RunId, e.WorkflowId));
    }

    public void Apply(RunCompletedEvent e)
    {
        var run = ActiveRuns.FirstOrDefault(r => r.RunId == e.RunId);
        if (run is null)
        {
            run = new RunChainVM(e.RunId, e.WorkflowId);
        }
        else
        {
            ActiveRuns.Remove(run);
        }

        run.Complete(e.Succeeded, e.Error);
        CompletedRuns.Insert(0, run);
    }

    public void ReconcileFromSnapshot(int activeRuns, int completedRuns, int failedRuns)
    {
        // Snapshot counters are the fallback when the Panel host opens after events have fired.
        while (ActiveRuns.Count < activeRuns)
            ActiveRuns.Add(new RunChainVM("snapshot-" + ActiveRuns.Count, ViewModelBase.TranslateTextWithSuffix("PanelHost", "FromSnapshot") ?? "(来自实例快照)"));
        while (ActiveRuns.Count > activeRuns)
            ActiveRuns.RemoveAt(ActiveRuns.Count - 1);

        var known = CompletedRuns.Count(r => r.IsFailed);
        while (known < failedRuns)
        {
            var failed = new RunChainVM("snapshot-failed-" + known, ViewModelBase.TranslateTextWithSuffix("PanelHost", "FromSnapshot") ?? "(来自实例快照)");
            failed.Complete(false, ViewModelBase.TranslateTextWithSuffix("PanelHost", "SnapshotCount") ?? "实例快照计数");
            CompletedRuns.Add(failed);
        }
        while (known > failedRuns && CompletedRuns.Count > 0)
        {
            var remove = CompletedRuns.Last(r => r.IsFailed);
            CompletedRuns.Remove(remove);
            known--;
        }
    }
}

/// <summary>Inline single-slot Dialog state (Bench UX v2 C27).</summary>
public sealed class PendingDialogVM : ReactiveObject
{
    public PendingDialogVM(string instanceId, string toolkitId, string controlId, string message, IReadOnlyList<string> buttons)
    {
        InstanceId = instanceId;
        ToolkitId = toolkitId;
        ControlId = controlId;
        Message = message;
        Buttons = buttons.Count > 0 ? buttons : [ViewModelBase.TranslateTextWithSuffix("PanelHost", "Ok") ?? "确定"];
    }

    public string InstanceId { get; }
    public string ToolkitId { get; }
    public string ControlId { get; }
    public string Message { get; }
    public IReadOnlyList<string> Buttons { get; }
}
