using System;
using System.Linq;
using System.Reactive;
using KitX.Dashboard.Services;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
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
        _runningInstances = toolkitService.ActiveFlowCounts().TryGetValue(model.GetId(), out var active) ? active : 0;
        ToggleMountCommand = ReactiveCommand.Create(ToggleMount);
        OpenBenchCommand = ReactiveCommand.Create(OpenBench);
        OpenSurfaceEditorCommand = ReactiveCommand.Create(OpenSurfaceEditor);
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

    /// <summary>Opens the card orchestrator for this ToolKit (surface editor, design P5).</summary>
    /// <remarks>
    /// <para><b>Not gated on <see cref="IsMounted"/>, deliberately.</b> This edits the declaration —
    /// the <c>surface.json</c> a ToolKit is installed with — and writing a declaration does not need
    /// the ToolKit to be running. The 2026-11-11 ruling that "editing a Surface only makes sense
    /// while the ToolKit is mounted" assumed the editor lived in the Surface client; once it moved to
    /// the Dashboard's orchestrator that premise went away (design §3, <c>Surface.md:816</c>).</para>
    ///
    /// <para><b>The save does not degrade when the ToolKit is unmounted, and the reason is worth
    /// stating because it was assumed otherwise.</b> The key check in
    /// <c>SurfaceEditorViewModel.Save</c> compares against the declarations handed to it by
    /// <c>App.axaml.cs</c>, which reads <c>IToolkitService.GetToolkit(id).Keys</c> — that call is
    /// <c>ToolkitStore.Load</c>, i.e. the config on disk, not the compiled table. So the declared
    /// keys are the same set mounted or not, and the check that a layout does not name a key the
    /// ToolKit stopped declaring works in both states.</para>
    ///
    /// <para>What is genuinely out of reach while unmounted is everything that needs the ToolKit to
    /// have been compiled at all — today that is <c>SurfaceValidator</c>'s repairs and warnings, which
    /// no production path runs on save either way (only the View attach path logs them). Nothing here
    /// hides that: the editor reports load-time errors and warnings it does have.</para>
    /// </remarks>
    public ReactiveCommand<Unit, Unit> OpenSurfaceEditorCommand { get; }

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

    private void OpenSurfaceEditor() => _windowService.OpenSurfaceEditorWindow(Model.GetId());
}
