using System;
using System.Text.Json;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>Panel host headless state tests: run-chain accumulation and single-slot Dialog state.</summary>
public class PanelRunTimelineTests
{
    private static RunStartedEvent Started(string instanceId, string runId, string workflowId)
        => new(Guid.NewGuid().ToString("N"), "tk", instanceId, DateTimeOffset.UtcNow, runId, workflowId);

    private static RunCompletedEvent Completed(string instanceId, string runId, string workflowId, bool ok, string? error)
        => new(Guid.NewGuid().ToString("N"), "tk", instanceId, DateTimeOffset.UtcNow, runId, workflowId, ok, error);

    [Fact]
    public void Timeline_Accumulates_ActiveAndCompletedRuns()
    {
        var timeline = new InstanceRunTimelineVM();
        timeline.Apply(Started("i1", "run1", "wf-a"));
        timeline.Apply(Started("i1", "run2", "wf-b"));

        Assert.Equal(2, timeline.ActiveRuns.Count);

        timeline.Apply(Completed("i1", "run1", "wf-a", false, "boom"));

        Assert.Single(timeline.ActiveRuns);
        var done = Assert.Single(timeline.CompletedRuns);
        Assert.Equal("run1", done.RunId);
        Assert.True(done.IsFailed);
        Assert.Equal("boom", done.Error);
    }

    [Fact]
    public void Timeline_IgnoresDuplicateRunStarted()
    {
        var timeline = new InstanceRunTimelineVM();
        var e = Started("i1", "run1", "wf");
        timeline.Apply(e);
        timeline.Apply(e);
        Assert.Single(timeline.ActiveRuns);
    }

    [Fact]
    public void PendingDialog_NormalizesEmptyButtonsToConfirm()
    {
        var dialog = new PendingDialogVM("i1", "tk", "dlg", "hello", []);
        Assert.Single(dialog.Buttons);
        Assert.Equal("确定", dialog.Buttons[0]);
    }

    [Fact]
    public void PendingDialog_PreservesMultipleButtons()
    {
        var dialog = new PendingDialogVM("i1", "tk", "dlg", "choose", ["是", "否", "取消"]);
        Assert.Equal(3, dialog.Buttons.Count);
        Assert.Equal("取消", dialog.Buttons[2]);
    }

    [Fact]
    public void RunChain_Complete_MarksFailureAndKeepsError()
    {
        var run = new RunChainVM("r1", "wf");
        run.Complete(false, "error text");
        Assert.False(run.IsActive);
        Assert.True(run.IsFailed);
        Assert.Equal("error text", run.Error);
    }
}
