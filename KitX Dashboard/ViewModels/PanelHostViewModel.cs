using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// ViewModel for the Panel host window — the ToolKit <b>use surface</b> (as opposed to the
/// Bench design surface). Lists every instance across mounted ToolKits (status / initiator /
/// run counters), lets the user end instances, and selects one for the panel view. Refresh
/// is driven by <see cref="IToolkitService.BenchEvent"/> (spawn/complete/cancel).
/// </summary>
internal class PanelHostViewModel : ViewModelBase, IDisposable
{
    private readonly IToolkitService _toolkitService;
    private readonly IBenchService _benchService;

    private readonly ObservableCollection<InstanceSnapshot> _instances = [];
    private InstanceSnapshot? _selectedInstance;

    internal PanelHostViewModel(IToolkitService toolkitService, IBenchService benchService)
    {
        _toolkitService = toolkitService;
        _benchService = benchService;

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
        }
    }

    /// <summary>True when an instance is selected (panel area visible).</summary>
    internal bool HasSelection => SelectedInstance is not null;

    /// <summary>True when no instance is selected (empty-state placeholder).</summary>
    internal bool IsEmpty => SelectedInstance is null;

    /// <summary>Summary line for the header.</summary>
    internal string Summary => $"{Instances.Count} 个实例";

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
    }

    private void OnBenchEvent(object? sender, BenchEvent e) => RefreshInstances();

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
    }
}
