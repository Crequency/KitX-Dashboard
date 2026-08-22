using System;
using System.Linq;
using System.Reactive;
using KitX.Dashboard.Services;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Models;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

/// <summary>
/// A single ToolKit card on the management page. Wraps the <see cref="Toolkit"/> model in an
/// observable façade so the card can show live mount state and drive mount/unmount + open-workbench
/// without the page VM re-selecting. The frontend depends only on the contract.
/// </summary>
public sealed class ToolkitCardVM : ReactiveObject
{
    private readonly IToolkitService _toolkitService;
    private readonly IWindowService _windowService;
    private bool _isMounted;
    private string? _errorMessage;
    private int _runningInstances;

    public ToolkitCardVM(Toolkit model, IToolkitService toolkitService, IWindowService windowService)
    {
        Model = model;
        _toolkitService = toolkitService;
        _windowService = windowService;
        _isMounted = toolkitService.IsMounted(model.GetId());
        _runningInstances = toolkitService.Instances.Count(i => i.ToolkitId == model.GetId() && i.Status == KitX.ToolKit.Instances.InstanceStatus.Running);
        ToggleMountCommand = ReactiveCommand.Create(ToggleMount);
        OpenBenchCommand = ReactiveCommand.Create(OpenBench);
    }

    /// <summary>The underlying config model.</summary>
    public Toolkit Model { get; }

    public string Name => Model.Meta.Name;
    public string Version => Model.Meta.Version;
    public string Description => Model.Meta.Description;
    public int WorkflowCount => Model.Workflows.Count;
    public int TriggerCount => Model.Triggers.Count;
    public int PluginCount => Model.Plugins.Count;

    /// <summary>Live mount state (drives the card's toggle).</summary>
    public bool IsMounted
    {
        get => _isMounted;
        set => this.RaiseAndSetIfChanged(ref _isMounted, value);
    }

    /// <summary>Last mount/unmount error (shown on the card).</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>Running instance count badge (Bench UX v2 G25b).</summary>
    public int RunningInstances
    {
        get => _runningInstances;
        set
        {
            this.RaiseAndSetIfChanged(ref _runningInstances, value);
            this.RaisePropertyChanged(nameof(HasRunningInstances));
            this.RaisePropertyChanged(nameof(RunningBadge));
        }
    }

    public bool HasRunningInstances => RunningInstances > 0;
    public string RunningBadge => $"{RunningInstances} 运行中";

    public ReactiveCommand<Unit, Unit> ToggleMountCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenBenchCommand { get; }

    private void ToggleMount()
    {
        try
        {
            // Decide direction from the SERVICE's real state, not the VM's IsMounted field:
            // the toggle's two-way IsChecked binding flips IsMounted before the command runs.
            var mounted = _toolkitService.IsMounted(Model.GetId());
            if (mounted)
                _toolkitService.Unmount(Model.GetId());
            else
                _toolkitService.Mount(Model.GetId());
            IsMounted = _toolkitService.IsMounted(Model.GetId());
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void OpenBench() => _windowService.OpenBenchWindow(Model);
}
