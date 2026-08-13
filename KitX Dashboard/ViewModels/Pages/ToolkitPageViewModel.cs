using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using KitX.Dashboard.Services;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Models;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

/// <summary>
/// ViewModel for the ToolKit management page (the future replacement for the workflow
/// management page). Backed by <see cref="IToolkitService"/> — CRUD against the real
/// <c>ToolkitStore</c>, mount/unmount (instance-model), and a per-ToolKit instance list.
/// The frontend depends only on the contract, never on implementation classes.
/// </summary>
internal class ToolkitPageViewModel : ViewModelBase, IDisposable
{
    private readonly IToolkitService _toolkitService;
    private readonly IBenchService _benchService;

    private readonly ObservableCollection<Toolkit> _toolkits = [];
    private Toolkit? _selectedToolkit;
    private IReadOnlyList<InstanceSnapshot> _instances = [];

    public ToolkitPageViewModel(IToolkitService toolkitService, IBenchService benchService)
    {
        _toolkitService = toolkitService;
        _benchService = benchService;

        InitCommands();
        InitEvents();

        EnsureSeedToolkit();
        RefreshToolkits();
    }

    /// <summary>Seeds a sample ToolKit on first run (empty store), so the management page
    /// is immediately usable. Persists via <see cref="IToolkitService.CreateToolkit"/>.</summary>
    private void EnsureSeedToolkit()
    {
        if (_toolkitService.ListToolkits().Count > 0)
            return;
        _toolkitService.CreateToolkit(ToolkitSampleFactory.Create());
    }

    /// <summary>All stored ToolKits (metadata).</summary>
    internal ObservableCollection<Toolkit> Toolkits => _toolkits;

    /// <summary>The ToolKit selected in the page's list.</summary>
    internal Toolkit? SelectedToolkit
    {
        get => _selectedToolkit;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedToolkit, value);
            this.RaisePropertyChanged(nameof(IsMounted));
            this.RaisePropertyChanged(nameof(MountButtonText));
            RefreshInstances();
        }
    }

    /// <summary>Instances of the selected ToolKit (snapshot).</summary>
    internal IReadOnlyList<InstanceSnapshot> Instances
    {
        get => _instances;
        private set => this.RaiseAndSetIfChanged(ref _instances, value);
    }

    /// <summary>True when the selected ToolKit is mounted.</summary>
    internal bool IsMounted => SelectedToolkit is not null && _toolkitService.IsMounted(SelectedToolkit.GetId());

    /// <summary>Mount/unmount button label.</summary>
    internal string MountButtonText => IsMounted ? "卸载" : "挂载";

    /// <summary>Instance summary for the selected ToolKit.</summary>
    internal string InstancesSummary => SelectedToolkit is null
        ? "未选择 ToolKit"
        : $"{Instances.Count} 个实例";

    /// <summary>Opens the Bench window for the selected ToolKit (design surface).</summary>
    internal ReactiveCommand<Unit, Unit>? OpenBenchCommand { get; set; }

    /// <summary>Creates a new ToolKit from a sample and persists it.</summary>
    internal ReactiveCommand<Unit, Unit>? CreateToolkitCommand { get; set; }

    /// <summary>Deletes the selected ToolKit (rejected while mounted).</summary>
    internal ReactiveCommand<Unit, Unit>? DeleteToolkitCommand { get; set; }

    /// <summary>Mounts/unmounts the selected ToolKit.</summary>
    internal ReactiveCommand<Unit, Unit>? ToggleMountCommand { get; set; }

    /// <summary>Opens the Panel host window (the instance use surface).</summary>
    internal ReactiveCommand<Unit, Unit>? OpenPanelHostCommand { get; set; }

    public override void InitCommands()
    {
        OpenBenchCommand = ReactiveCommand.Create(() =>
        {
            if (SelectedToolkit is null)
                return;
            // Tell the Bench design window which ToolKit it is showing, then open it.
            UIStateService.BenchToolkit = SelectedToolkit;
            UIStateService.ShowWindow(new Views.BenchWindow(), UIStateService.MainWindow);
        });

        CreateToolkitCommand = ReactiveCommand.Create(() =>
        {
            var toolkit = ToolkitSampleFactory.Create($"New ToolKit {Toolkits.Count + 1}");
            _toolkitService.CreateToolkit(toolkit);
            RefreshToolkits();
        });

        DeleteToolkitCommand = ReactiveCommand.Create(() =>
        {
            if (SelectedToolkit is null)
                return;
            _toolkitService.DeleteToolkit(SelectedToolkit.GetId());
            SelectedToolkit = null;
            RefreshToolkits();
        });

        ToggleMountCommand = ReactiveCommand.Create(() =>
        {
            if (SelectedToolkit is null)
                return;
            if (IsMounted)
                _toolkitService.Unmount(SelectedToolkit.GetId());
            else
                _toolkitService.Mount(SelectedToolkit.GetId());
            this.RaisePropertyChanged(nameof(IsMounted));
            this.RaisePropertyChanged(nameof(MountButtonText));
            RefreshInstances();
        });

        OpenPanelHostCommand = ReactiveCommand.Create(() =>
        {
            UIStateService.ShowWindow(new Views.PanelHostWindow(), UIStateService.MainWindow);
        });
    }

    public override void InitEvents()
    {
        _toolkitService.ToolkitListChanged += OnToolkitListChanged;
        _toolkitService.BenchEvent += OnBenchEvent;
    }

    private void OnToolkitListChanged(object? sender, EventArgs e) => RefreshToolkits();

    private void OnBenchEvent(object? sender, BenchEvent e) => RefreshInstances();

    private void RefreshToolkits()
    {
        Toolkits.Clear();
        foreach (var toolkit in _toolkitService.ListToolkits())
            Toolkits.Add(toolkit);
        this.RaisePropertyChanged(nameof(IsMounted));
        this.RaisePropertyChanged(nameof(MountButtonText));
        RefreshInstances();
    }

    private void RefreshInstances()
    {
        Instances = SelectedToolkit is null
            ? []
            : _toolkitService.Instances.Where(i => i.ToolkitId == SelectedToolkit.GetId()).ToList();
        this.RaisePropertyChanged(nameof(InstancesSummary));
    }

    /// <summary>Unsubscribes event handlers (D11 page Unloaded-dispose pattern).</summary>
    public void Dispose()
    {
        _toolkitService.ToolkitListChanged -= OnToolkitListChanged;
        _toolkitService.BenchEvent -= OnBenchEvent;
    }
}
