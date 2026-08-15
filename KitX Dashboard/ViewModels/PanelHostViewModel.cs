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
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>A group of instances belonging to one ToolKit (the panel host's left tree).</summary>
public sealed class InstanceGroupVM : ReactiveObject
{
    private int _runningCount;

    public InstanceGroupVM(string toolkitId, string name)
    {
        ToolkitId = toolkitId;
        Name = name;
    }

    public string ToolkitId { get; }
    public string Name { get; }

    public ObservableCollection<InstanceSnapshot> Instances { get; } = [];

    public int RunningCount
    {
        get => _runningCount;
        set => this.RaiseAndSetIfChanged(ref _runningCount, value);
    }

    public bool HasRunning => RunningCount > 0;
}

/// <summary>
/// ViewModel for the Panel host window — the ToolKit <b>use surface</b> (as opposed to the
/// Bench design surface). Shows a grouped instance tree (by ToolKit), renders the selected
/// instance's panel controls, monitors run chains, and owns the single-slot Dialog queue.
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
    private readonly Dictionary<string, InstanceRunTimelineVM> _timelines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingDialogVM> _pendingDialogs = new(StringComparer.Ordinal);

    private InstanceSnapshot? _selectedInstance;
    private PendingDialogVM? _pendingDialog;
    private bool _hasPanel;
    private bool _isPickerVisible;
    private Toolkit? _selectedToolkit;
    private Trigger? _selectedTrigger;
    private string _payloadText = string.Empty;
    private string? _pickerMessage;
    private string? _spawnRejectedMessage;
    private int _selectedTab;
    private bool _isLogPaused;

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

    /// <summary>Raised when the host window should present itself (without stealing focus).</summary>
    internal event Action? PanelOpenRequested;

    /// <summary>All instances across mounted ToolKits (flat, for selection logic).</summary>
    internal ObservableCollection<InstanceSnapshot> Instances => _instances;

    /// <summary>Instances grouped by ToolKit (the left tree).</summary>
    internal ObservableCollection<InstanceGroupVM> InstanceGroups => _instanceGroups;

    internal InstanceSnapshot? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedInstance, value);
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(NoPanel));
            this.RaisePropertyChanged(nameof(ActiveRuns));
            this.RaisePropertyChanged(nameof(CompletedRuns));
            PendingDialog = value is null ? null : _pendingDialogs.GetValueOrDefault(value.InstanceId);
            BuildPanel();
        }
    }

    internal bool HasSelection => SelectedInstance is not null;

    internal bool IsEmpty => SelectedInstance is null;

    internal bool NoPanel => SelectedInstance is not null && !HasPanel;

    internal ObservableCollection<PanelControlViewModel> PanelControls => _panelControls;

    internal bool HasPanel
    {
        get => _hasPanel;
        private set => this.RaiseAndSetIfChanged(ref _hasPanel, value);
    }

    /// <summary>Selected detail tab: 0 = panel, 1 = runs (Bench UX v2 §5.3).</summary>
    internal int SelectedTab
    {
        get => _selectedTab;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTab, value);
            this.RaisePropertyChanged(nameof(IsPanelTabSelected));
            this.RaisePropertyChanged(nameof(IsRunTabSelected));
        }
    }

    internal bool IsPanelTabSelected => SelectedTab == 0;
    internal bool IsRunTabSelected => SelectedTab == 1;

    internal bool IsLogPaused
    {
        get => _isLogPaused;
        set => this.RaiseAndSetIfChanged(ref _isLogPaused, value);
    }

    // ── Trigger selector (new instance) ──

    internal IReadOnlyList<Toolkit> MountedToolkits { get; private set; } = [];

    internal IReadOnlyList<Trigger> ManualTriggers { get; private set; } = [];

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

    internal bool HasPickerMessage => !string.IsNullOrWhiteSpace(PickerMessage);

    /// <summary>Tree-top spawn rejection notice (MaxInstances / unmounted).</summary>
    internal string? SpawnRejectedMessage
    {
        get => _spawnRejectedMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _spawnRejectedMessage, value);
            this.RaisePropertyChanged(nameof(HasSpawnRejectedMessage));
        }
    }

    internal bool HasSpawnRejectedMessage => !string.IsNullOrWhiteSpace(SpawnRejectedMessage);

    internal PendingDialogVM? PendingDialog
    {
        get => _pendingDialog;
        private set => this.RaiseAndSetIfChanged(ref _pendingDialog, value);
    }

    internal bool HasPendingDialog => PendingDialog is not null;

    internal ObservableCollection<RunChainVM> ActiveRuns =>
        SelectedInstance is null ? [] : (_timelines.TryGetValue(SelectedInstance.InstanceId, out var t) ? t.ActiveRuns : []);

    internal ObservableCollection<RunChainVM> CompletedRuns =>
        SelectedInstance is null ? [] : (_timelines.TryGetValue(SelectedInstance.InstanceId, out var t) ? t.CompletedRuns : []);

    internal string Summary => string.Format(
        TranslateTextWithSuffix("PanelHost", "RunSummary") ?? "{0} 个实例 · {1} 运行中",
        Instances.Count, Instances.Count(i => i.Status == KitX.ToolKit.Instances.InstanceStatus.Running));

    internal ReactiveCommand<InstanceSnapshot, Unit>? EndInstanceCommand { get; set; }
    internal ReactiveCommand<Unit, Unit>? EndAllCommand { get; set; }
    internal ReactiveCommand<Unit, Unit>? OpenBenchCommand { get; set; }
    internal ReactiveCommand<Unit, Unit>? TogglePickerCommand { get; set; }
    internal ReactiveCommand<Unit, Unit>? NewInstanceCommand { get; set; }
    internal ReactiveCommand<string, Unit>? ConfirmDialogCommand { get; set; }
    internal ReactiveCommand<int, Unit>? SelectTabCommand { get; set; }

    public override void InitCommands()
    {
        EndInstanceCommand = ReactiveCommand.CreateFromTask<InstanceSnapshot, Unit>(async snapshot =>
        {
            if (snapshot is null)
                return Unit.Default;
            if (snapshot.Status == KitX.ToolKit.Instances.InstanceStatus.Running)
            {
                var result = await MessageBoxManager.GetMessageBoxStandard(
                    "结束实例", $"确定要结束实例 {snapshot.InstanceId[..Math.Min(12, snapshot.InstanceId.Length)]} 吗？",
                    ButtonEnum.YesNo, Icon.Warning).ShowWindowAsync();
                if (result != ButtonResult.Yes)
                    return Unit.Default;
            }

            _benchService.EndInstance(snapshot.InstanceId);
            SpawnRejectedMessage = null;
            return Unit.Default;
        });

        EndAllCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (Instances.Count == 0)
                return;
            var result = await MessageBoxManager.GetMessageBoxStandard(
                "结束全部", $"确定要结束全部 {Instances.Count} 个实例吗？",
                ButtonEnum.YesNo, Icon.Warning).ShowWindowAsync();
            if (result != ButtonResult.Yes)
                return;
            _benchService.EndAll();
            SpawnRejectedMessage = null;
        });

        OpenBenchCommand = ReactiveCommand.Create(() =>
        {
            var toolkit = SelectedInstance is null
                ? null
                : _toolkitService.GetToolkit(SelectedInstance.ToolkitId);
            if (toolkit is not null)
                Services.UIStateService.OpenBenchWindow(toolkit);
            else
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
                var message = TranslateTextWithSuffix("PanelHost", "PickerSpawnRejected") ?? "启动失败：可能已达最大实例数或工具箱未挂载";
                PickerMessage = message;
                SpawnRejectedMessage = message;
                return;
            }

            PickerMessage = null;
            SpawnRejectedMessage = null;
            IsPickerVisible = false;
            FocusInstance(instanceId, openPanel: true);
        });

        ConfirmDialogCommand = ReactiveCommand.Create<string>(button =>
        {
            if (PendingDialog is null)
                return;
            _panelRuntime.RaiseControlEvent(PendingDialog.InstanceId, PendingDialog.ControlId, "Confirm", button);
            _pendingDialogs.Remove(PendingDialog.InstanceId);
            PendingDialog = null;
        });

        SelectTabCommand = ReactiveCommand.Create<int>(tab => SelectedTab = tab);
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
        switch (e)
        {
            case InstanceSpawnedEvent spawned:
                RefreshInstances();
                var toolkit = _toolkitService.GetToolkit(spawned.ToolkitId);
                var trigger = toolkit?.Triggers.FirstOrDefault(t => t.Id == spawned.TriggerId);
                var openPanel = trigger?.Config?.Surface is null or "auto";
                FocusInstance(spawned.InstanceId, openPanel);
                if (openPanel)
                    PanelOpenRequested?.Invoke();
                break;
            case PanelOpenRequestedEvent open:
                FocusInstance(open.InstanceId, openPanel: true);
                PanelOpenRequested?.Invoke();
                break;
            case DialogRequestedEvent dialog:
                _pendingDialogs[dialog.InstanceId] = new PendingDialogVM(
                    dialog.InstanceId, dialog.ToolkitId, dialog.ControlId, dialog.Message, dialog.Buttons);
                if (SelectedInstance?.InstanceId == dialog.InstanceId)
                    PendingDialog = _pendingDialogs[dialog.InstanceId];
                break;
            case RunStartedEvent runStarted:
                Timeline(runStarted.InstanceId).Apply(runStarted);
                if (SelectedInstance?.InstanceId == runStarted.InstanceId)
                {
                    this.RaisePropertyChanged(nameof(ActiveRuns));
                    this.RaisePropertyChanged(nameof(CompletedRuns));
                }
                break;
            case RunCompletedEvent runCompleted:
                Timeline(runCompleted.InstanceId).Apply(runCompleted);
                if (SelectedInstance?.InstanceId == runCompleted.InstanceId)
                {
                    this.RaisePropertyChanged(nameof(ActiveRuns));
                    this.RaisePropertyChanged(nameof(CompletedRuns));
                }
                break;
            case InstanceCompletedEvent or InstanceCancelledEvent:
                RefreshInstances();
                break;
        }

        UpdatePanel(e);
    }

    private InstanceRunTimelineVM Timeline(string instanceId)
    {
        if (!_timelines.TryGetValue(instanceId, out var timeline))
        {
            timeline = new InstanceRunTimelineVM();
            _timelines[instanceId] = timeline;
        }

        return timeline;
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

    /// <summary>Cross-window focus request: refresh tree, select instance, optionally show panel.</summary>
    internal void FocusInstance(string instanceId, bool openPanel)
    {
        RefreshInstances();
        SelectedInstance = Instances.FirstOrDefault(i => i.InstanceId == instanceId);
        if (openPanel)
            SelectedTab = 0;
    }

    private void RefreshInstances()
    {
        var selectedId = SelectedInstance?.InstanceId;
        Instances.Clear();
        foreach (var snapshot in _toolkitService.Instances)
        {
            Instances.Add(snapshot);
            Timeline(snapshot.InstanceId).ReconcileFromSnapshot(snapshot.ActiveRuns, snapshot.CompletedRuns, snapshot.FailedRuns);
        }

        InstanceGroups.Clear();
        foreach (var group in Instances.GroupBy(i => i.ToolkitId))
        {
            var toolkit = _toolkitService.GetToolkit(group.Key);
            var vm = new InstanceGroupVM(group.Key, toolkit?.Meta.Name ?? group.Key);
            foreach (var snapshot in group)
                vm.Instances.Add(snapshot);
            vm.RunningCount = group.Count(i => i.Status == KitX.ToolKit.Instances.InstanceStatus.Running);
            vm.RaisePropertyChanged(nameof(InstanceGroupVM.HasRunning));
            InstanceGroups.Add(vm);
        }

        var stillThere = Instances.FirstOrDefault(i => i.InstanceId == selectedId);
        SelectedInstance = stillThere;
        this.RaisePropertyChanged(nameof(Summary));
    }

    /// <summary>Unsubscribes event handlers (D11 window Unloaded-dispose pattern).</summary>
    public void Dispose()
    {
        _toolkitService.BenchEvent -= OnBenchEvent;
        _eventService.Unsubscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }
}
