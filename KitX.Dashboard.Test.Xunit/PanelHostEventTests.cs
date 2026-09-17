using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using KitX.Core.Contract.Event;
using KitX.Dashboard.Converters;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Panel host VM wiring tests (UX v2 C24/C25/C27): Bench events flowing through
/// <see cref="PanelHostViewModel.OnBenchEvent"/> into observable VM state, on the UI
/// thread (headless Avalonia).
/// </summary>
public class PanelHostEventTests
{
    private static InstanceSnapshot Snapshot(
        string instanceId,
        string toolkitId = "tk",
        InstanceStatus status = InstanceStatus.Running,
        int activeRuns = 0,
        int completedRuns = 0,
        int failedRuns = 0)
        => new(instanceId, toolkitId, "manual", Initiator.Unknown, status, DateTimeOffset.UtcNow, null,
            activeRuns, completedRuns, failedRuns);

    private static Toolkit Toolkit() => new()
    {
        Id = "tk",
        Meta = new ToolkitMeta { Name = "demo" },
    };

    private static InstanceSpawnedEvent Spawned(string toolkit, string instance)
        => new(Guid.NewGuid().ToString("N"), toolkit, instance, DateTimeOffset.UtcNow, "manual", Initiator.Unknown, default);

    private static RunStartedEvent RunStarted(string instance, string runId)
        => new(Guid.NewGuid().ToString("N"), "tk", instance, DateTimeOffset.UtcNow, runId, "wf");

    private static RunCompletedEvent RunCompleted(string instance, string runId, bool ok, string? error)
        => new(Guid.NewGuid().ToString("N"), "tk", instance, DateTimeOffset.UtcNow, runId, "wf", ok, error);

    private static InstanceCompletedEvent Completed(string toolkit, string instance)
        => new(Guid.NewGuid().ToString("N"), toolkit, instance, DateTimeOffset.UtcNow, true);

    private static InstanceCancelledEvent Cancelled(string toolkit, string instance)
        => new(Guid.NewGuid().ToString("N"), toolkit, instance, DateTimeOffset.UtcNow);

    // ── Regression (2026-08-20): in-place row replacement must not tear down the
    // selection/panel, and a rebuilt panel must hydrate its Log control's history. ──

    private static Toolkit ToolkitWithLogPanel() => new()
    {
        Id = "tk",
        Meta = new ToolkitMeta { Name = "demo" },
        UiPanel = new UiPanel { Controls = [new UiControl { Type = "Log", Id = "log" }] },
    };

    [AvaloniaFact]
    public void RunEvent_OnSelectedInstance_KeepsSelectionAndLiveLog()
    {
        // A run event replaces the selected row's snapshot in place; the UI selection
        // model pushes a transient null through the TwoWay binding when the row item
        // vanishes. The VM must swallow it: the panel controls (and their live Log
        // entries) survive, and the selection is re-pointed at the fresh snapshot.
        var service = new FakeToolkitService([ToolkitWithLogPanel()], [Snapshot("inst-1")]);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());
        vm.SelectedInstance = service.Instances.First();

        var logVm = vm.PanelControls.Single(c => c.Type == "Log");
        logVm.Apply("log", JsonSerializer.SerializeToElement("live-entry"));

        service.Replace(Snapshot("inst-1", activeRuns: 1));
        service.Raise(RunStarted("inst-1", "run-1"));

        Assert.Equal("inst-1", vm.SelectedInstance?.InstanceId);
        Assert.Same(logVm, vm.PanelControls.Single(c => c.Type == "Log"));
        Assert.Contains("live-entry", logVm.LogEntries);
    }

    [AvaloniaFact]
    public void RebuiltPanel_SeedsLogHistory_FromRuntime()
    {
        // The live log projection only delivers future entries, so a panel rebuilt for
        // an instance that already logged must seed its history from the runtime.
        var service = new FakeToolkitService([ToolkitWithLogPanel()], [Snapshot("inst-1")]);
        var runtime = new FakePanelRuntime();
        runtime.SeedLog("inst-1", "log", ["hist-1", "hist-2"]);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), runtime, new FakeEventService());

        vm.SelectedInstance = service.Instances.First();

        var logVm = vm.PanelControls.Single(c => c.Type == "Log");
        Assert.Equal(new[] { "hist-1", "hist-2" }, logVm.LogEntries);
    }

    [AvaloniaFact]
    public void DialogRequestedEvent_PopulatesPendingDialog()
    {
        var toolkit = Toolkit();
        var service = new FakeToolkitService([toolkit], [Snapshot("inst-1")]);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());
        vm.SelectedInstance = service.Instances.First();

        service.Raise(new DialogRequestedEvent(
            Guid.NewGuid().ToString("N"), "tk", "inst-1", DateTimeOffset.UtcNow, "ask", "choose", ["是", "否"]));

        Assert.True(vm.HasPendingDialog);
        Assert.NotNull(vm.PendingDialog);
        Assert.Equal("choose", vm.PendingDialog!.Message);
        Assert.Equal(["是", "否"], vm.PendingDialog.Buttons);
    }

    [AvaloniaFact]
    public void SpawnRejectedEvent_SetsNotice_AndKeepsSelection()
    {
        var toolkit = Toolkit();
        var snapshot = Snapshot("inst-1");
        var service = new FakeToolkitService([toolkit], [snapshot]);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());
        vm.SelectedInstance = snapshot;

        service.Raise(new InstanceSpawnRejectedEvent(
            Guid.NewGuid().ToString("N"), "tk", string.Empty, DateTimeOffset.UtcNow, "manual", "MaxInstances=1"));

        Assert.True(vm.HasSpawnRejectedMessage);
        Assert.Same(snapshot, vm.SelectedInstance); // rejection must not clear the current selection
    }

    [AvaloniaFact]
    public void RunEvents_AccumulateActiveAndCompletedRuns()
    {
        var toolkit = Toolkit();
        var snapshot = Snapshot("inst-1");
        var service = new FakeToolkitService([toolkit], [snapshot]);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());
        vm.SelectedInstance = snapshot;

        service.Replace(Snapshot("inst-1", activeRuns: 1));
        service.Raise(new RunStartedEvent(
            Guid.NewGuid().ToString("N"), "tk", "inst-1", DateTimeOffset.UtcNow, "run-1", "wf"));
        Assert.Single(vm.ActiveRuns);

        service.Replace(Snapshot("inst-1", completedRuns: 1, failedRuns: 1));
        service.Raise(new RunCompletedEvent(
            Guid.NewGuid().ToString("N"), "tk", "inst-1", DateTimeOffset.UtcNow, "run-1", "wf", false, "boom"));

        Assert.Empty(vm.ActiveRuns);
        var done = Assert.Single(vm.CompletedRuns);
        Assert.Equal("boom", done.Error);
    }

    [Fact]
    public void BoolToFontWeightConverter_NeverProducesZeroWeight()
    {
        // Regression guard for the panel-host freeze: a bool bound straight to FontWeight
        // became FontWeight=0 and Avalonia's text layout threw "Font weight must be > 0".
        var converter = BoolToFontWeightConverter.Instance;
        Assert.Equal(FontWeight.SemiBold, converter.Convert(true, typeof(FontWeight), null, CultureInfo.InvariantCulture));
        Assert.Equal(FontWeight.Normal, converter.Convert(false, typeof(FontWeight), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SelectTabCommand_AcceptsStringParameterFromXaml()
    {
        // Regression guard for the run-tab freeze: XAML CommandParameter="1" is a string;
        // ReactiveCommand<int> threw and froze the render loop (dump.log).
        var toolkit = Toolkit();
        var service = new FakeToolkitService([toolkit], [Snapshot("inst-1")]);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());

        ((System.Windows.Input.ICommand)vm.SelectTabCommand!).Execute("1");
        Assert.Equal(1, vm.SelectedTab);

        ((System.Windows.Input.ICommand)vm.SelectTabCommand).Execute("0");
        Assert.Equal(0, vm.SelectedTab);
    }

    // ── G2: Log ring cap ──

    [Fact]
    public void PanelControlLog_RingsAtConfiguredLimit()
    {
        var control = new UiControl { Type = "Log", Id = "log" };
        var vm = new PanelControlViewModel(control, "inst-1", new FakePanelRuntime(), logLimit: 3);

        for (var i = 1; i <= 5; i++)
            vm.Apply("log", JsonSerializer.SerializeToElement("line-" + i));

        // Oldest entries are trimmed as new ones append past the cap.
        Assert.Equal(3, vm.LogEntries.Count);
        Assert.Equal("line-3", vm.LogEntries[0]);
        Assert.Equal("line-5", vm.LogEntries[^1]);
    }

    [Fact]
    public void PanelControlLog_DefaultLimit_MatchesConfigDefault()
    {
        var control = new UiControl { Type = "Log", Id = "log" };
        var vm = new PanelControlViewModel(control, "inst-1", new FakePanelRuntime());

        for (var i = 1; i <= 1001; i++)
            vm.Apply("log", JsonSerializer.SerializeToElement("line-" + i));

        // The default cap (1000) is the config default Config_Performance.PanelLogLimit.
        Assert.Equal(1000, vm.LogEntries.Count);
        Assert.Equal("line-2", vm.LogEntries[0]);
        Assert.Equal("line-1001", vm.LogEntries[^1]);
    }

    // ── G3: Incremental vs full-refresh consistency ──

    [AvaloniaFact]
    public void IncrementalEvents_ConsistentWithFullRefresh()
    {
        // Toolkit "tk" (known) + "tkB" (unknown to the mounted set — exercises on-demand grouping).
        var toolkitA = Toolkit();
        var toolkitB = new Toolkit { Id = "tkB", Meta = new ToolkitMeta { Name = "tkb" } };
        var service = new FakeToolkitService([toolkitA, toolkitB], []);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());

        // Drive a realistic mixed sequence through the incremental event path.
        service.Add(Snapshot("a-1", toolkitId: "tk"));
        service.Raise(Spawned("tk", "a-1"));

        service.Add(Snapshot("a-2", toolkitId: "tk"));
        service.Raise(Spawned("tk", "a-2"));

        // Unknown toolkit spawn → on-demand group creation.
        service.Add(Snapshot("b-1", toolkitId: "tkB"));
        service.Raise(Spawned("tkB", "b-1"));

        // Run events only touch the row counters (collection structure unchanged).
        service.Replace(Snapshot("a-1", toolkitId: "tk", activeRuns: 1));
        service.Raise(RunStarted("a-1", "run-1"));

        service.Replace(Snapshot("b-1", toolkitId: "tkB", completedRuns: 1, failedRuns: 0));
        service.Raise(RunCompleted("b-1", "run-x", true, null));

        // Instance completed → row retained with Completed status.
        service.Replace(Snapshot("a-2", toolkitId: "tk", status: InstanceStatus.Completed, completedRuns: 2));
        service.Raise(Completed("tk", "a-2"));

        // Instance cancelled → row removed; its group deleted when empty.
        service.Remove("a-1");
        service.Raise(Cancelled("tk", "a-1"));

        // A second VM rebuilt wholesale from the identical final service state must match.
        var full = new FakeToolkitService([toolkitA, toolkitB], [.. service.Instances]);
        var fullVm = new PanelHostViewModel(full, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());
        fullVm.RefreshInstances();

        AssertEquivalentTree(vm, fullVm);
    }

    /// <summary>Compares flat list and grouped tree field-by-field between two VMs.</summary>
    private static void AssertEquivalentTree(PanelHostViewModel a, PanelHostViewModel b)
    {
        Assert.Equal(
            a.Instances.Select(i => (i.InstanceId, i.ToolkitId, i.Status, i.ActiveRuns, i.CompletedRuns, i.FailedRuns)),
            b.Instances.Select(i => (i.InstanceId, i.ToolkitId, i.Status, i.ActiveRuns, i.CompletedRuns, i.FailedRuns)));

        Assert.Equal(
            a.InstanceGroups.Select(g => (g.ToolkitId, g.Name, g.RunningCount, string.Join(",", g.Instances.Select(i => i.InstanceId)))),
            b.InstanceGroups.Select(g => (g.ToolkitId, g.Name, g.RunningCount, string.Join(",", g.Instances.Select(i => i.InstanceId)))));
    }

    // ── Fakes ──

    private sealed class FakeToolkitService(List<Toolkit> toolkits, List<InstanceSnapshot> instances) : IToolkitService
    {
        public event EventHandler? ToolkitListChanged;
        public event EventHandler<BenchEvent>? BenchEvent;

        public void Raise(BenchEvent e) => BenchEvent?.Invoke(this, e);

        public void Replace(InstanceSnapshot snapshot)
        {
            var idx = instances.FindIndex(i => i.InstanceId == snapshot.InstanceId);
            if (idx >= 0)
                instances[idx] = snapshot;
            else
                instances.Add(snapshot);
        }

        public void Add(InstanceSnapshot snapshot) => instances.Add(snapshot);

        public void Remove(string instanceId) => instances.RemoveAll(i => i.InstanceId == instanceId);

        public IReadOnlyList<Toolkit> ListToolkits() => toolkits;
        public Toolkit? GetToolkit(string toolkitId) => toolkits.FirstOrDefault(t => t.GetId() == toolkitId);
        public Toolkit CreateToolkit(Toolkit draft) => draft;
        public Toolkit UpdateToolkit(Toolkit toolkit) => toolkit;
        public bool DeleteToolkit(string toolkitId) => false;
        public void Mount(string toolkitId) { }
        public void Unmount(string toolkitId) { }
        public IReadOnlyList<string> MountedToolkitIds { get; } = [];
        public bool IsMounted(string toolkitId) => false;
        public IReadOnlyList<InstanceSnapshot> Instances => instances;
    }

    private sealed class FakeBenchService : IBenchService
    {
        public event EventHandler<BenchEvent>? Event;
        public string? Spawn(string toolkitId, string triggerId, object? payload = null, Initiator? initiator = null) => null;
        public void EndInstance(string instanceId) { }
        public void EndAll() { }
        public ConfigValidationResult Validate(Toolkit toolkit) => new();
    }

    private sealed class FakePanelRuntime : IPanelRuntime
    {
        private readonly Dictionary<string, JsonElement> _values = new();

        public JsonElement? GetControlValue(string instanceId, string controlId)
            => _values.TryGetValue(instanceId + "/" + controlId, out var v) ? v : null;

        public void SetControlValue(string instanceId, string controlId, object? value)
            => _values[instanceId + "/" + controlId] = JsonSerializer.SerializeToElement(value);

        public void RaiseControlEvent(string instanceId, string controlId, string eventName, object? value) { }
        public void RequestPanelOpen(string instanceId) { }

        private readonly Dictionary<string, IReadOnlyList<string>> _logs = new();
        public IReadOnlyList<string> GetControlLog(string instanceId, string controlId)
            => _logs.TryGetValue(instanceId + "/" + controlId, out var v) ? v : [];
        public void SeedLog(string instanceId, string controlId, IReadOnlyList<string> entries)
            => _logs[instanceId + "/" + controlId] = entries;
    }

    private sealed class FakeEventService : IEventService
    {
        public void Subscribe(string eventName, EventHandler<EventArgs> handler) { }
        public void Unsubscribe(string eventName, EventHandler<EventArgs> handler) { }
        public void Publish(string eventName, EventArgs args) { }
        public void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs { }
        public void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs { }
        public void Publish<TEventArgs>(string eventName, TEventArgs args) where TEventArgs : EventArgs { }
        public void Subscribe<TEvent>(Action<TEvent> handler) { }
        public void Unsubscribe<TEvent>(Action<TEvent> handler) { }
        public void Publish<TEvent>(TEvent payload) { }
    }
}
