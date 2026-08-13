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
    private bool _isMounted;

    public ToolkitCardVM(Toolkit model, IToolkitService toolkitService)
    {
        Model = model;
        _toolkitService = toolkitService;
        _isMounted = toolkitService.IsMounted(model.GetId());
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

    public ReactiveCommand<Unit, Unit> ToggleMountCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenBenchCommand { get; }

    private void ToggleMount()
    {
        if (IsMounted)
            _toolkitService.Unmount(Model.GetId());
        else
            _toolkitService.Mount(Model.GetId());
        IsMounted = _toolkitService.IsMounted(Model.GetId());
    }

    private void OpenBench() => UIStateService.OpenBenchWindow(Model);
}
