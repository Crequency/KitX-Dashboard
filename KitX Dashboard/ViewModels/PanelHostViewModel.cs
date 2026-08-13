using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using KitX.Core.Contract.Event;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Models;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>A group of instances belonging to one ToolKit (the panel host's left tree).</summary>
public sealed class InstanceGroupVM : ReactiveObject
{
    public InstanceGroupVM(string toolkitId, string name)
    {
        ToolkitId = toolkitId;
        Name = name;
    }

    public string ToolkitId { get; }
    public string Name { get; }
    public ObservableCollection<InstanceSnapshot> Instances { get; } = [];
}

/// <summary>
/// ViewModel for the Panel host window — the ToolKit <b>use surface</b> (as opposed to the
/// Bench design surface). Shows a grouped instance tree (by ToolKit), renders the selected
/// instance's panel controls (a projection of its DataStore keys), and starts new instances
/// via a trigger selector (mounted ToolKit → Manual trigger → optional payload).
/// </summary>
internal class PanelHostViewModel : ViewModelBase, IDisposable
{
    private readonly IToolkitService _toolkitService;
    private readonly IBenchService _benchService;
    private readonly IPanelRuntime _panelRuntime;
    private readonly IEventService _eventService;

    private readonly ObservableCollection<InstanceSnapshot> _instances = [];
    private readonly ObservableCollection<InstanceGroupVM> _instanceGroups = [];
    private readonly ObservableCollection<PanelControlViewModel> _panelControls = [];
    private InstanceSnapshot? _selectedInstance;
    private bool _hasPanel;
    private bool _isPickerVisible;
    private Toolkit? _selectedToolkit;
    private Trigger? _selectedTrigger;
    private string _payloadText = string.Empty;
    private string? _pickerMessage;

    public PanelHostViewModel(IToolkitService toolkitService, IBenchService benchService, IPanelRuntime panelRuntime, IEventService eventService)
    {
        _toolkitService = toolkitService;
        _benchService = benchService;
        _panelRuntime = panelRuntime;
        _eventService = eventService;

        InitCommands();
        InitEvents();

        RefreshInstances();
    }

    /// <summary>All instances across mounted ToolKits (flat, for selection logic).</summary>
    internal ObservableCollection<InstanceSnapshot> Instances => _instances;

    /// <summary>Instances grouped by ToolKit (the left tree).</summary>
    internal ObservableCollection<InstanceGroupVM> InstanceGroups => _instanceGroups;

    /// <summary>The instance selected for the panel view.</summary>
    internal InstanceSnapshot? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedInstance, value);
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(NoPanel));
            BuildPanel();
        }
    }

    /// <summary>True when an instance is selected (panel area visible).</summary>
    internal bool HasSelection => SelectedInstance is not null;

    /// <summary>True when no instance is selected (empty-state placeholder).</summary>
    internal bool IsEmpty => SelectedInstance is null;

    /// <summary>True when an instance is selected but its ToolKit declares no panel.</summary>
    internal bool NoPanel => SelectedInstance is not null && !HasPanel;

    /// <summary>Panel controls of the selected instance (built from its toolkit's UiPanel).</summary>
    internal ObservableCollection<PanelControlViewModel> PanelControls => _panelControls;

    /// <summary>True when the selected instance's ToolKit declares a panel.</summary>
    internal bool HasPanel
    {
        get => _hasPanel;
        private set => this.RaiseAndSetIfChanged(ref _hasPanel, value);
    }

    // ── Trigger selector (new instance) ──

    /// <summary>Mounted ToolKits available to spawn from.</summary>
    internal IReadOnlyList<Toolkit> MountedToolkits { get; private set; } = [];

    /// <summary>Manual triggers of the selected mounted ToolKit.</summary>
    internal IReadOnlyList<Trigger> ManualTriggers { get; private set; } = [];

    /// <summary>True when the trigger selector is expanded.</summary>
    internal bool IsPickerVisible
    {
        get => _isPickerVisible;
        set => this.RaiseAndSetIfChanged(ref _isPickerVisible, value);
    }

    internal Toolkit? SelectedToolkit
    {
        get => _selectedToolkit;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedToolkit, value);
            RefreshManualTriggers();
        }
    }

    internal Trigger? SelectedTrigger
    {
        get => _selectedTrigger;
        set => this.RaiseAndSetIfChanged(ref _selectedTrigger, value);
    }

    internal string PayloadText
    {
        get => _payloadText;
        set => this.RaiseAndSetIfChanged(ref _payloadText, value);
    }

    internal string? PickerMessage
    {
        get => _pickerMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _pickerMessage, value);
            this.RaisePropertyChanged(nameof(HasPickerMessage));
        }
    }

    /// <summary>True when the picker has a message to show.</summary>
    internal bool HasPickerMessage => !string.IsNullOrWhiteSpace(PickerMessage);

    /// <summary>Summary line for the header.</summary>
    internal string Summary => string.Format(
        TranslateTextWithSuffix("PanelHost", "InstanceCount") ?? "{0} 个实例", Instances.Count);

    /// <summary>Ends the selected instance.</summary>
    internal ReactiveCommand<Unit, Unit>? EndInstanceCommand { get; set; }

    /// <summary>Ends every instance.</summary>
    internal ReactiveCommand<Unit, Unit>? EndAllCommand { get; set; }

    /// <summary>Opens the Bench design surface (context switch).</summary>
    internal ReactiveCommand<Unit, Unit>? OpenBenchCommand { get; set; }

    /// <summary>Toggles the trigger selector and refreshes mounted ToolKits.</summary>
    internal ReactiveCommand<Unit, Unit>? TogglePickerCommand { get; set; }

    /// <summary>Spawns a new instance from the selected trigger (+ optional payload).</summary>
    internal ReactiveCommand<Unit, Unit>? NewInstanceCommand { get; set; }

    public override void InitCommands()
    {
        EndInstanceCommand = ReactiveCommand.Create(() =>
        {
            if (SelectedInstance is null)
                return;
            _benchService.EndInstance(SelectedInstance.InstanceId);
        });

        EndAllCommand = ReactiveCommand.Create(() => _benchService.EndAll());

        OpenBenchCommand = ReactiveCommand.Create(() =>
        {
            Services.UIStateService.ShowWindow(new Views.BenchWindow(), Services.UIStateService.MainWindow);
        });

        TogglePickerCommand = ReactiveCommand.Create(() =>
        {
            IsPickerVisible = !IsPickerVisible;
            if (IsPickerVisible)
            {
                RefreshMountedToolkits();
                PickerMessage = null;
            }
        });

        NewInstanceCommand = ReactiveCommand.Create(() =>
        {
            if (SelectedToolkit is null || SelectedTrigger is null)
            {
                PickerMessage = TranslateTextWithSuffix("PanelHost", "PickerSelectRequired") ?? "请选择工具箱与手动触发器";
                return;
            }

            object? payload = null;
            if (!string.IsNullOrWhiteSpace(PayloadText))
            {
                try
                {
                    payload = JsonSerializer.Deserialize<object>(PayloadText);
                }
                catch
                {
                    PickerMessage = TranslateTextWithSuffix("PanelHost", "PickerInvalidPayload") ?? "payload 不是合法 JSON";
                    return;
                }
            }

            var instanceId = _benchService.Spawn(SelectedToolkit.GetId(), SelectedTrigger.Id, payload);
            if (instanceId is null)
            {
                PickerMessage = TranslateTextWithSuffix("PanelHost", "PickerSpawnRejected") ?? "启动失败：可能已达最大实例数或工具箱未挂载";
                return;
            }

            PickerMessage = null;
            IsPickerVisible = false;
            RefreshInstances();
            SelectedInstance = Instances.FirstOrDefault(i => i.InstanceId == instanceId);
        });
    }

    public override void InitEvents()
    {
        _toolkitService.BenchEvent += OnBenchEvent;
        _eventService.Subscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => this.RaisePropertyChanged(nameof(Summary));

    private void OnBenchEvent(object? sender, BenchEvent e)
    {
        RefreshInstances();
        UpdatePanel(e);
    }

    private void BuildPanel()
    {
        _panelControls.Clear();
        var toolkit = SelectedInstance is null ? null : _toolkitService.GetToolkit(SelectedInstance.ToolkitId);
        var panel = toolkit?.UiPanel;
        HasPanel = panel is not null;
        if (panel is null || SelectedInstance is null)
            return;
        foreach (var control in panel.Controls)
            _panelControls.Add(new PanelControlViewModel(control, SelectedInstance.InstanceId, _panelRuntime));
    }

    private void UpdatePanel(BenchEvent e)
    {
        if (SelectedInstance is null)
            return;

        switch (e)
        {
            case UiControlStateChangedEvent uie when uie.InstanceId == SelectedInstance.InstanceId:
                ApplyToControl(uie.ControlId, uie.Prop, uie.Value);
                break;
            case DataStoreChangedEvent dse when dse.InstanceId == SelectedInstance.InstanceId
                                                    && TryParsePanelKey(dse.Key, out var controlId, out var prop):
                ApplyToControl(controlId, prop, dse.NewValue);
                break;
        }
    }

    private void ApplyToControl(string controlId, string prop, object? value)
    {
        var control = _panelControls.FirstOrDefault(c => c.Id == controlId);
        control?.Apply(prop, value is JsonElement je ? je : null);
    }

    /// <summary>Parses <c>.../{instanceId}/panel/{controlId}/{prop}</c> (or the bare suffix) into controlId + prop.</summary>
    private static bool TryParsePanelKey(string key, out string controlId, out string prop)
    {
        controlId = string.Empty;
        prop = string.Empty;
        var idx = key.LastIndexOf("/panel/", StringComparison.Ordinal);
        if (idx < 0)
            return false;
        var seg = key[(idx + "/panel/".Length)..].Split('/');
        if (seg.Length < 2)
            return false;
        controlId = seg[0];
        prop = seg[1];
        return true;
    }

    private void RefreshMountedToolkits()
    {
        MountedToolkits = _toolkitService.MountedToolkitIds
            .Select(id => _toolkitService.GetToolkit(id))
            .Where(t => t is not null)
            .Cast<Toolkit>()
            .ToList();
        this.RaisePropertyChanged(nameof(MountedToolkits));
        SelectedToolkit = MountedToolkits.FirstOrDefault();
    }

    private void RefreshManualTriggers()
    {
        ManualTriggers = SelectedToolkit?.Triggers.Where(t => t.Type == TriggerType.Manual).ToList() ?? [];
        this.RaisePropertyChanged(nameof(ManualTriggers));
        SelectedTrigger = ManualTriggers.FirstOrDefault();
    }

    private void RefreshInstances()
    {
        var selectedId = SelectedInstance?.InstanceId;
        Instances.Clear();
        foreach (var snapshot in _toolkitService.Instances)
            Instances.Add(snapshot);

        // Rebuild the grouped tree.
        InstanceGroups.Clear();
        foreach (var group in Instances.GroupBy(i => i.ToolkitId))
        {
            var toolkit = _toolkitService.GetToolkit(group.Key);
            var vm = new InstanceGroupVM(group.Key, toolkit?.Meta.Name ?? group.Key);
            foreach (var snapshot in group)
                vm.Instances.Add(snapshot);
            InstanceGroups.Add(vm);
        }

        SelectedInstance = Instances.FirstOrDefault(i => i.InstanceId == selectedId);
        this.RaisePropertyChanged(nameof(Summary));
    }

    /// <summary>Unsubscribes event handlers (D11 window Unloaded-dispose pattern).</summary>
    public void Dispose()
    {
        _toolkitService.BenchEvent -= OnBenchEvent;
        _eventService.Unsubscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }
}
