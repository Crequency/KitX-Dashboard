using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Models;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;

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
    private readonly int _panelLogLimit;

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

    /// <param name="configService">Optional config provider for the per-control Log ring
    /// cap. Left null (headless tests) it falls back to the configured default.</param>
    public PanelHostViewModel(IToolkitService toolkitService, IBenchService benchService, IPanelRuntime panelRuntime, IEventService eventService, IConfigService? configService = null)
    {
        _toolkitService = toolkitService;
        _benchService = benchService;
        _panelRuntime = panelRuntime;
        _eventService = eventService;
        _panelLogLimit = configService?.AppConfig.Performance.PanelLogLimit ?? 1000;

        InitCommands();
        InitEvents();

        RefreshInstances();
        Log.Information(
            "[PanelHostViewModel] Created — instances:{Instances.Count} groups:{InstanceGroups.Count} " +
            "selected:" + (SelectedInstance?.InstanceId ?? "‹null›"));
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
            var previous = _selectedInstance;
            // A refreshed snapshot of the SAME instance (e.g. after a run event) must not
            // tear down the panel controls: that would lose live Log entries and UI values.
            var sameInstance = value is not null && previous is not null && value.InstanceId == previous.InstanceId;
            this.RaiseAndSetIfChanged(ref _selectedInstance, value);
            if (sameInstance)
                HasPanel = _toolkitService.GetToolkit(value!.ToolkitId)?.UiPanel is not null;
            else
                BuildPanel();
            PendingDialog = value is null ? null : _pendingDialogs.GetValueOrDefault(value.InstanceId);
            // Raise the dependent notifications AFTER BuildPanel/HasPanel have settled so
            // NoPanel/IsEmpty read the final panel state.
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(NoPanel));
            this.RaisePropertyChanged(nameof(ActiveRuns));
            this.RaisePropertyChanged(nameof(CompletedRuns));
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
    internal ReactiveCommand<string, Unit>? SelectTabCommand { get; set; }

    public override void InitCommands()
    {
        EndInstanceCommand = ReactiveCommand.CreateFromTask<InstanceSnapshot, Unit>(async snapshot =>
        {
            if (snapshot is null)
                return Unit.Default;
            if (snapshot.Status == KitX.ToolKit.Instances.InstanceStatus.Running)
            {
                var result = await MessageBoxManager.GetMessageBoxStandard(
                    TranslateTextWithSuffix("PanelHost", "EndInstanceTitle") ?? "结束实例",
                    string.Format(TranslateTextWithSuffix("PanelHost", "EndInstanceConfirm") ?? "确定要结束实例 {0} 吗？", snapshot.InstanceId[..Math.Min(12, snapshot.InstanceId.Length)]),
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
                TranslateTextWithSuffix("PanelHost", "EndAllTitle") ?? "结束全部",
                string.Format(TranslateTextWithSuffix("PanelHost", "EndAllConfirm") ?? "确定要结束全部 {0} 个实例吗？", Instances.Count),
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

        // XAML CommandParameter="0/1" arrives as a STRING. Accept a string and parse it;
        // a ReactiveCommand<int> throws "Command requires parameters of type System.Int32,
        // but received parameter of type System.String" and froze the panel host when the
        // run tab was clicked (see dump.log).
        SelectTabCommand = ReactiveCommand.Create<string>(tab =>
        {
            if (int.TryParse(tab, out var index))
                SelectedTab = index;
            else
                Log.Warning("[PanelHostViewModel] SelectTabCommand received non-numeric parameter '{Parameter}'", tab);
        });
    }

    public override void InitEvents()
    {
        _toolkitService.BenchEvent += OnBenchEvent;
        _eventService.Subscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => this.RaisePropertyChanged(nameof(Summary));

    /// <summary>
    /// Bench events can originate on Timer callbacks or workflow background threads;
    /// marshal every event onto the UI thread before touching observable collections
    /// (design invariant: events are dispatched uniformly via Dispatcher.UIThread).
    /// </summary>
    private void OnBenchEvent(object? sender, BenchEvent e)
    {
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
            HandleBenchEvent(sender, e);
        else
            dispatcher.Post(() => HandleBenchEvent(sender, e));
    }

    private void HandleBenchEvent(object? sender, BenchEvent e)
    {
        switch (e)
        {
            case InstanceSpawnedEvent spawned when string.IsNullOrWhiteSpace(spawned.InstanceId):
                // Legacy rejection encoding: never select/clear anything, only surface the notice.
                SpawnRejectedMessage = BuildSpawnRejectedMessage(spawned.ToolkitId);
                Log.Warning("[PanelHostViewModel] Spawn event with empty instance id treated as rejection (toolkit {Toolkit})",
                    spawned.ToolkitId);
                break;
            case InstanceSpawnedEvent spawned:
                Log.Information("[PanelHostViewModel] InstanceSpawned {Instance} (toolkit {Toolkit}, trigger {Trigger})",
                    spawned.InstanceId, spawned.ToolkitId, spawned.TriggerId);
                ReconcileInstanceFromService(spawned.InstanceId);
                var toolkit = _toolkitService.GetToolkit(spawned.ToolkitId);
                var trigger = toolkit?.Triggers.FirstOrDefault(t => t.Id == spawned.TriggerId);
                var openPanel = trigger?.Config?.Surface is null or "auto";
                FocusInstance(spawned.InstanceId, openPanel);
                if (openPanel)
                    PanelOpenRequested?.Invoke();
                break;
            case InstanceSpawnRejectedEvent rejected:
                SpawnRejectedMessage = BuildSpawnRejectedMessage(rejected.ToolkitId, rejected.Reason);
                Log.Warning("[PanelHostViewModel] Spawn rejected for toolkit {Toolkit}: {Reason}",
                    rejected.ToolkitId, rejected.Reason);
                break;
            case PanelOpenRequestedEvent open:
                Log.Information("[PanelHostViewModel] PanelOpenRequested {Instance}", open.InstanceId);
                FocusInstance(open.InstanceId, openPanel: true);
                PanelOpenRequested?.Invoke();
                break;
            case DialogRequestedEvent dialog:
                Log.Information("[PanelHostViewModel] DialogRequested {Instance}/{Control}: {Message}",
                    dialog.InstanceId, dialog.ControlId, dialog.Message);
                _pendingDialogs[dialog.InstanceId] = new PendingDialogVM(
                    dialog.InstanceId, dialog.ToolkitId, dialog.ControlId, dialog.Message, dialog.Buttons);
                if (SelectedInstance?.InstanceId == dialog.InstanceId)
                    PendingDialog = _pendingDialogs[dialog.InstanceId];
                break;
            case RunStartedEvent runStarted:
                Log.Information("[PanelHostViewModel] RunStarted {Instance}/{Run}/{Workflow}",
                    runStarted.InstanceId, runStarted.RunId, runStarted.WorkflowId);
                Timeline(runStarted.InstanceId).Apply(runStarted);
                ReconcileInstanceFromService(runStarted.InstanceId);
                break;
            case RunCompletedEvent runCompleted:
                Log.Information("[PanelHostViewModel] RunCompleted {Instance}/{Run}/{Workflow} succeeded={Succeeded}",
                    runCompleted.InstanceId, runCompleted.RunId, runCompleted.WorkflowId, runCompleted.Succeeded);
                Timeline(runCompleted.InstanceId).Apply(runCompleted);
                ReconcileInstanceFromService(runCompleted.InstanceId);
                break;
            case InstanceCompletedEvent completed:
                Log.Information("[PanelHostViewModel] InstanceCompleted {Instance} succeeded={Succeeded}",
                    completed.InstanceId, completed.Succeeded);
                MarkInstanceCompleted(completed.InstanceId);
                break;
            case InstanceCancelledEvent cancelled:
                Log.Information("[PanelHostViewModel] InstanceCancelled {Instance}", cancelled.InstanceId);
                RemoveInstance(cancelled.InstanceId);
                break;
        }

        UpdatePanel(e);
    }

    /// <summary>Tree-top spawn-rejection notice (MaxInstances / unmounted, C25).</summary>
    private string BuildSpawnRejectedMessage(string toolkitId, string? reason = null)
    {
        var toolkitName = _toolkitService.GetToolkit(toolkitId)?.Meta?.Name ?? toolkitId;
        return string.IsNullOrWhiteSpace(reason)
            ? string.Format(TranslateTextWithSuffix("PanelHost", "SpawnRejectedLimit") ?? "实例启动被拒绝：{0} 已达最大实例数限制", toolkitName)
            : string.Format(TranslateTextWithSuffix("PanelHost", "SpawnRejectedReason") ?? "实例启动被拒绝：{0}（{1}）", toolkitName, reason);
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
        {
            var vm = new PanelControlViewModel(control, SelectedInstance.InstanceId, _panelRuntime, _panelLogLimit);
            // Hydrate the live value from the DataStore (a workflow may have written it
            // before the user opened/selected this instance; C26).
            if (_panelRuntime.GetControlValue(SelectedInstance.InstanceId, control.Id) is { } initial)
                vm.Apply("value", initial);
            _panelControls.Add(vm);
        }
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
        }
    }

    private void ApplyToControl(string controlId, string prop, object? value)
    {
        var control = _panelControls.FirstOrDefault(c => c.Id == controlId);
        control?.Apply(prop, value is JsonElement je ? je : null);
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

    /// <summary>Cross-window focus request: select instance, optionally show panel. The
    /// instance should already be known to this VM via the incremental event path; a full
    /// reconciliation is only a defensive fallback for out-of-order requests.</summary>
    internal void FocusInstance(string instanceId, bool openPanel)
    {
        if (Instances.FirstOrDefault(i => i.InstanceId == instanceId) is null)
            RefreshInstances();
        SelectedInstance = Instances.FirstOrDefault(i => i.InstanceId == instanceId);
        if (openPanel)
            SelectedTab = 0;
    }

    /// <summary>
    /// Full reconciliation entry point: rebuilds the flat list, grouped tree, selection and
    /// summary wholesale from <see cref="IToolkitService.Instances"/>. Kept as the defensive
    /// entry for initial load, out-of-order events and unknown cases; the event-driven
    /// incremental path must produce the same final state as this method.
    /// </summary>
    internal void RefreshInstances()
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

    // ── Incremental, event-driven instance updates (G3) ──
    // Each Bench event mutates only the affected row/group instead of rebuilding the whole
    // tree. These paths must keep the VM's observable collections equal to a full
    // <see cref="RefreshInstances"/> rebuild for the same underlying service state.

    /// <summary>Reconcile one instance's row from the service state; fall back to a full
    /// refresh if the service does not yet know it (out-of-order event).</summary>
    private void ReconcileInstanceFromService(string instanceId)
    {
        var snapshot = _toolkitService.Instances.FirstOrDefault(i => i.InstanceId == instanceId);
        if (snapshot is null)
        {
            RefreshInstances();
            return;
        }
        UpsertInstance(snapshot);
    }

    /// <summary>Transition one row to <see cref="KitX.ToolKit.Instances.InstanceStatus.Completed"/>
    /// in place (retained for later review, never removed).</summary>
    private void MarkInstanceCompleted(string instanceId)
    {
        var snapshot = _toolkitService.Instances.FirstOrDefault(i => i.InstanceId == instanceId);
        if (snapshot is null)
        {
            RefreshInstances();
            return;
        }
        if (snapshot.Status != KitX.ToolKit.Instances.InstanceStatus.Completed)
            snapshot = snapshot with { Status = KitX.ToolKit.Instances.InstanceStatus.Completed };
        UpsertInstance(snapshot);
    }

    /// <summary>Insert or replace an instance in the flat list and its toolkit group,
    /// creating the group on demand (it may be a not-yet-mounted toolkit).</summary>
    private void UpsertInstance(InstanceSnapshot snapshot)
    {
        var flatIdx = IndexOf(_instances, snapshot.InstanceId);
        if (flatIdx >= 0)
            _instances[flatIdx] = snapshot;
        else
            _instances.Add(snapshot);

        var group = _instanceGroups.FirstOrDefault(g => g.ToolkitId == snapshot.ToolkitId);
        if (group is null)
        {
            var toolkit = _toolkitService.GetToolkit(snapshot.ToolkitId);
            group = new InstanceGroupVM(snapshot.ToolkitId, toolkit?.Meta.Name ?? snapshot.ToolkitId);
            _instanceGroups.Add(group);
        }
        var groupIdx = IndexOf(group.Instances, snapshot.InstanceId);
        if (groupIdx >= 0)
            group.Instances[groupIdx] = snapshot;
        else
            group.Instances.Add(snapshot);

        RecomputeGroupRunning(group);

        // Keep the selection pointing at the freshest snapshot of the selected instance
        // without tearing down its panel controls (the VM's same-instance guard handles that).
        if (SelectedInstance?.InstanceId == snapshot.InstanceId)
            SelectedInstance = snapshot;

        this.RaisePropertyChanged(nameof(Summary));
    }

    /// <summary>Remove an instance and its toolkit group when the group becomes empty.</summary>
    private void RemoveInstance(string instanceId)
    {
        var flatIdx = IndexOf(_instances, instanceId);
        if (flatIdx >= 0)
        {
            _instances.RemoveAt(flatIdx);
            if (SelectedInstance?.InstanceId == instanceId)
                SelectedInstance = null;
        }

        for (var gi = _instanceGroups.Count - 1; gi >= 0; gi--)
        {
            var group = _instanceGroups[gi];
            var idx = IndexOf(group.Instances, instanceId);
            if (idx < 0)
                continue;
            group.Instances.RemoveAt(idx);
            RecomputeGroupRunning(group);
            if (group.Instances.Count == 0)
                _instanceGroups.RemoveAt(gi);
        }

        this.RaisePropertyChanged(nameof(Summary));
    }

    private void RecomputeGroupRunning(InstanceGroupVM group)
    {
        group.RunningCount = group.Instances.Count(i => i.Status == KitX.ToolKit.Instances.InstanceStatus.Running);
        group.RaisePropertyChanged(nameof(InstanceGroupVM.HasRunning));
    }

    private static int IndexOf(ObservableCollection<InstanceSnapshot> list, string instanceId)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i].InstanceId == instanceId)
                return i;
        return -1;
    }

    /// <summary>Unsubscribes event handlers (D11 window Unloaded-dispose pattern).</summary>
    public void Dispose()
    {
        _toolkitService.BenchEvent -= OnBenchEvent;
        _eventService.Unsubscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }
}
