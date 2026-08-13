using System;
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

/// <summary>
/// ViewModel for the Panel host window — the ToolKit <b>use surface</b> (as opposed to the
/// Bench design surface). Lists every instance across mounted ToolKits (status / initiator /
/// run counters), lets the user end instances, and renders the selected instance's panel
/// controls (a projection of its DataStore keys, updated via the Bench event channel).
/// </summary>
internal class PanelHostViewModel : ViewModelBase, IDisposable
{
    private readonly IToolkitService _toolkitService;
    private readonly IBenchService _benchService;
    private readonly IPanelRuntime _panelRuntime;
    private readonly IEventService _eventService;

    private readonly ObservableCollection<InstanceSnapshot> _instances = [];
    private readonly ObservableCollection<PanelControlViewModel> _panelControls = [];
    private InstanceSnapshot? _selectedInstance;
    private bool _hasPanel;

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

    /// <summary>All instances across mounted ToolKits.</summary>
    internal ObservableCollection<InstanceSnapshot> Instances => _instances;

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

    /// <summary>Summary line for the header.</summary>
    internal string Summary => string.Format(
        TranslateTextWithSuffix("PanelHost", "InstanceCount") ?? "{0} 个实例", Instances.Count);

    /// <summary>Ends the selected instance.</summary>
    internal ReactiveCommand<Unit, Unit>? EndInstanceCommand { get; set; }

    /// <summary>Ends every instance.</summary>
    internal ReactiveCommand<Unit, Unit>? EndAllCommand { get; set; }

    /// <summary>Opens the Bench design surface (context switch).</summary>
    internal ReactiveCommand<Unit, Unit>? OpenBenchCommand { get; set; }

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

    private void RefreshInstances()
    {
        var selectedId = SelectedInstance?.InstanceId;
        Instances.Clear();
        foreach (var snapshot in _toolkitService.Instances)
            Instances.Add(snapshot);
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
