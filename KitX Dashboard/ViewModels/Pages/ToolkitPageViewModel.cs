using System;
using System.Collections.ObjectModel;
using System.Reactive;
using KitX.Dashboard.Services;
using KitX.ToolKit.Models;
using KitX.ToolKit.Triggers;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

/// <summary>
/// ViewModel for the ToolKit management page — the future replacement for the workflow
/// management page. Scaffolding only ("not officially released"): it lists ToolKits from
/// the shared <see cref="UIStateService.Toolkits"/> collection and can activate one in the
/// <see cref="BenchTriggerManager"/> and open its Bench window. There is no ToolKit storage
/// service yet, so the list is seeded with a built-in sample.
/// </summary>
internal class ToolkitPageViewModel : ViewModelBase, IDisposable
{
    private readonly BenchTriggerManager _benchManager;

    private Toolkit? _selectedToolkit;

    /// <summary>The shared ToolKit list (re-export of <see cref="UIStateService.Toolkits"/>).</summary>
    internal ObservableCollection<Toolkit> Toolkits => UIStateService.Toolkits;

    /// <summary>The ToolKit selected in the page's list.</summary>
    internal Toolkit? SelectedToolkit
    {
        get => _selectedToolkit;
        set => this.RaiseAndSetIfChanged(ref _selectedToolkit, value);
    }

    /// <summary>Opens the Bench window for the selected ToolKit (activates it in the bench manager).</summary>
    internal ReactiveCommand<Unit, Unit>? OpenBenchCommand { get; set; }

    /// <summary>Adds a new in-memory sample ToolKit to the list.</summary>
    internal ReactiveCommand<Unit, Unit>? CreateToolkitCommand { get; set; }

    public ToolkitPageViewModel(BenchTriggerManager benchManager)
    {
        _benchManager = benchManager;

        InitCommands();
        InitEvents();

        EnsureSampleToolkits();
    }

    public override void InitCommands()
    {
        OpenBenchCommand = ReactiveCommand.Create(() =>
        {
            if (SelectedToolkit is null)
                return;

            // Activate the ToolKit in the singleton orchestration entry point, then open
            // its Bench window. Activate validates the config (throws if invalid).
            _benchManager.Activate(SelectedToolkit);
            UIStateService.ShowWindow(new Views.BenchWindow(), UIStateService.MainWindow);
        });

        CreateToolkitCommand = ReactiveCommand.Create(() =>
        {
            var toolkit = ToolkitSampleFactory.Create($"New ToolKit {Toolkits.Count + 1}");
            Toolkits.Add(toolkit);
            SelectedToolkit = toolkit;
        });
    }

    public override void InitEvents()
    {
    }

    private void EnsureSampleToolkits()
    {
        if (Toolkits.Count == 0)
            Toolkits.Add(ToolkitSampleFactory.Create());
    }

    /// <summary>No event subscriptions today; present for the page's Unloaded-dispose pattern.</summary>
    public void Dispose()
    {
    }
}
