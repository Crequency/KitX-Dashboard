using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using KitX.Core.Contract.Event;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Window-service unit tests (post DI migration of the retired static window coordinator).
/// <para>
/// The service's <see cref="WindowService"/> constructor-injects nothing (no ViewModels —
/// a VM dependency there would create the DI cycle that surfaces as a frozen UI), so it can
/// be constructed directly in headless tests. Real windows that resolve their DataContext via
/// the DI container (BenchWindow / PanelHostWindow / WorkflowEditorWindowV6) cannot be
/// instantiated here, so the <see cref="WindowService.RegisterWorkflowEditor"/> seam and the
/// static <see cref="WindowService.FocusPanelDataContext"/> routing are the tested surfaces.
/// </para>
/// </summary>
public class WindowServiceTests
{
    [AvaloniaFact]
    public void WorkflowEditorWindows_Registers_And_Removes_On_Close()
    {
        var svc = new WindowService();
        var window = new Window();
        svc.RegisterWorkflowEditor("wf-1", window);

        Assert.True(svc.WorkflowEditorWindows.ContainsKey("wf-1"));
        Assert.Same(window, svc.WorkflowEditorWindows["wf-1"]);

        // Registering the same id again overwrites (the Close-removal still targets that id).
        var second = new Window();
        svc.RegisterWorkflowEditor("wf-1", second);
        Assert.Same(second, svc.WorkflowEditorWindows["wf-1"]);

        // Closing the registered window removes the entry (Closed-removal semantics).
        second.Show();
        second.Close();
        Assert.DoesNotContain("wf-1", svc.WorkflowEditorWindows);
    }

    [AvaloniaFact]
    public void FocusPanelData_Routes_To_PanelHostViewModel()
    {
        var service = new FakeToolkitService([Toolkit()], [Snapshot("inst-1")]);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());

        // Nothing selected before routing.
        Assert.Null(vm.SelectedInstance);

        WindowService.FocusPanelDataContext(vm, "inst-1", openPanel: true);

        Assert.Equal("inst-1", vm.SelectedInstance?.InstanceId);
        Assert.Equal(0, vm.SelectedTab); // openPanel selects the panel tab
    }

    [AvaloniaFact]
    public void FocusPanelData_UnknownInstance_DoesNotSelect()
    {
        var service = new FakeToolkitService([Toolkit()], [Snapshot("inst-1")]);
        var vm = new PanelHostViewModel(service, new FakeBenchService(), new FakePanelRuntime(), new FakeEventService());

        WindowService.FocusPanelDataContext(vm, "inst-missing", openPanel: true);

        Assert.Null(vm.SelectedInstance);
    }

    // ── helpers ──

    private static InstanceSnapshot Snapshot(string instanceId)
        => new(instanceId, "tk", "manual", Initiator.Unknown, InstanceStatus.Running, DateTimeOffset.UtcNow, null, 0, 0, 0);

    private static Toolkit Toolkit() => new()
    {
        Id = "tk",
        Meta = new ToolkitMeta { Name = "demo" },
    };

    private sealed class FakeToolkitService(List<Toolkit> toolkits, List<InstanceSnapshot> instances) : IToolkitService
    {
        public event EventHandler? ToolkitListChanged;
        public event EventHandler<BenchEvent>? BenchEvent;

        public void Raise(BenchEvent e) => BenchEvent?.Invoke(this, e);

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
