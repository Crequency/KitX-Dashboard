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
        InstanceStatus status = InstanceStatus.Running,
        int activeRuns = 0,
        int completedRuns = 0,
        int failedRuns = 0)
        => new(instanceId, "tk", "manual", Initiator.Unknown, status, DateTimeOffset.UtcNow, null,
            activeRuns, completedRuns, failedRuns);

    private static Toolkit Toolkit() => new()
    {
        Id = "tk",
        Meta = new ToolkitMeta { Name = "demo" },
    };

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

    // ── Fakes ──

    private sealed class FakeToolkitService(List<Toolkit> toolkits, List<InstanceSnapshot> instances) : IToolkitService
    {
        public event EventHandler? ToolkitListChanged;
        public event EventHandler<BenchEvent>? BenchEvent;

        public void Raise(BenchEvent e) => BenchEvent?.Invoke(this, e);

        public void Replace(InstanceSnapshot snapshot) => instances[0] = snapshot;

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
    }

    private sealed class FakeEventService : IEventService
    {
        public void Subscribe(string eventName, EventHandler<EventArgs> handler) { }
        public void Unsubscribe(string eventName, EventHandler<EventArgs> handler) { }
        public void Publish(string eventName, EventArgs args) { }
        public void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs { }
        public void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs { }
        public void Publish<TEventArgs>(string eventName, TEventArgs args) where TEventArgs : EventArgs { }
    }
}
