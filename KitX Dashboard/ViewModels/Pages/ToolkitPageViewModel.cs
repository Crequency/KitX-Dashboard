using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using KitX.Core.Contract.Event;
using KitX.Dashboard.Services;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Models;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

/// <summary>
/// ViewModel for the ToolKit management page — the <b>repository</b> surface: list / create /
/// delete / mount ToolKits, and open a ToolKit's workbench (one window per ToolKit). Backed by
/// <see cref="IToolkitService"/> (CRUD against the real <c>ToolkitStore</c>). Instance monitoring
/// lives on the Panel host (use surface), not here.
/// </summary>
internal class ToolkitPageViewModel : ViewModelBase, IDisposable
{
    private readonly IToolkitService _toolkitService;
    private readonly IEventService _eventService;

    private readonly ObservableCollection<ToolkitCardVM> _cards = [];

    public ToolkitPageViewModel(IToolkitService toolkitService, IBenchService benchService, IEventService eventService)
    {
        _toolkitService = toolkitService;
        _eventService = eventService;

        InitCommands();
        InitEvents();

        RefreshCards();
    }

    /// <summary>All stored ToolKits as cards.</summary>
    internal ObservableCollection<ToolkitCardVM> Cards => _cards;

    /// <summary>True when the store is empty (empty-state banner).</summary>
    internal bool IsEmpty => _cards.Count == 0;

    /// <summary>Inverse of <see cref="IsEmpty"/> (card grid visibility).</summary>
    internal bool HasCards => !IsEmpty;

    /// <summary>Creates a new ToolKit from a sample and persists it.</summary>
    internal ReactiveCommand<Unit, Unit>? CreateToolkitCommand { get; set; }

    /// <summary>Deletes a ToolKit (rejected while mounted).</summary>
    internal ReactiveCommand<ToolkitCardVM, Unit>? DeleteToolkitCommand { get; set; }

    public override void InitCommands()
    {
        CreateToolkitCommand = ReactiveCommand.Create(() =>
        {
            // Create a blank ToolKit the user fills in on the workbench (no auto-seed;
            // tutorial toolkits come later with the tutorial module).
            var toolkit = new Toolkit
            {
                Meta = new ToolkitMeta
                {
                    Name = $"New ToolKit {_cards.Count + 1}",
                    Version = "1.0.0",
                    Author = "KitX",
                },
                Workflows = [],
                Plugins = [],
                Triggers = [],
            };
            _toolkitService.CreateToolkit(toolkit);
            RefreshCards();
        });

        DeleteToolkitCommand = ReactiveCommand.Create<ToolkitCardVM>(card =>
        {
            if (card is null)
                return;
            _toolkitService.DeleteToolkit(card.Model.GetId());
            RefreshCards();
        });
    }

    public override void InitEvents()
    {
        _toolkitService.ToolkitListChanged += OnToolkitListChanged;
        _toolkitService.BenchEvent += OnBenchEventDispatch;
        _eventService.Subscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Card display strings are model-driven; nothing to refresh here.
    }

    private void OnToolkitListChanged(object? sender, EventArgs e) => RefreshCards();

    /// <summary>
    /// Bench events can arrive on Timer/workflow background threads; marshal to the UI
    /// thread before touching card VMs/observable state (design invariant).
    /// </summary>
    private void OnBenchEventDispatch(object? sender, KitX.ToolKit.Contracts.Events.BenchEvent e)
    {
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
            OnBenchEvent(sender, e);
        else
            dispatcher.Post(() => OnBenchEvent(sender, e));
    }

    private void OnBenchEvent(object? sender, KitX.ToolKit.Contracts.Events.BenchEvent e)
    {
        foreach (var card in _cards)
            card.RunningInstances = _toolkitService.Instances.Count(i =>
                i.ToolkitId == card.Model.GetId() && i.Status == KitX.ToolKit.Instances.InstanceStatus.Running);
    }

    private void RefreshCards()
    {
        _cards.Clear();
        foreach (var toolkit in _toolkitService.ListToolkits())
            _cards.Add(new ToolkitCardVM(toolkit, _toolkitService));
        this.RaisePropertyChanged(nameof(IsEmpty));
        this.RaisePropertyChanged(nameof(HasCards));
    }

    /// <summary>Unsubscribes event handlers (D11 page Unloaded-dispose pattern).</summary>
    public void Dispose()
    {
        _toolkitService.ToolkitListChanged -= OnToolkitListChanged;
        _toolkitService.BenchEvent -= OnBenchEventDispatch;
        _eventService.Unsubscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }
}
