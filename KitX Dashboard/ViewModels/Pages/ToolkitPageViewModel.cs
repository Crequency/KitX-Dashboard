using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using KitX.Core.Contract.Event;
using KitX.Dashboard.Services;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Contract.Declaration;
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
    private readonly IWindowService _windowService;

    private readonly ObservableCollection<ToolkitCardVM> _cards = [];

    public ToolkitPageViewModel(IToolkitService toolkitService, IRunService runService, IEventService eventService, IWindowService windowService)
    {
        _toolkitService = toolkitService;
        _eventService = eventService;
        _windowService = windowService;

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

        DeleteToolkitCommand = ReactiveCommand.CreateFromTask<ToolkitCardVM>(DeleteToolkitAsync);
    }

    public override void InitEvents()
    {
        _toolkitService.ToolkitListChanged += OnToolkitListChanged;
        _toolkitService.ToolkitEvent += OnToolkitEventDispatch;
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
    private void OnToolkitEventDispatch(object? sender, KitX.ToolKit.Contract.Events.ToolkitEvent e)
    {
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
            OnToolkitEvent(sender, e);
        else
            dispatcher.Post(() => OnToolkitEvent(sender, e));
    }

    private void OnToolkitEvent(object? sender, KitX.ToolKit.Contract.Events.ToolkitEvent e)
    {
        // Re-read the snapshot on every event rather than interpreting `e`. The count has an
        // authority (the runtime's scheduler), so an event is a nudge, not a data source
        // (ADR-0015 D5.1). That also keeps this correct for events it cannot interpret.
        var counts = _toolkitService.ActiveFlowCounts();
        foreach (var card in _cards)
            card.RunningInstances = counts.TryGetValue(card.Model.GetId(), out var active) ? active : 0;
    }

    /// <summary>
    /// Asks, then deletes — in that order, and nothing else in between.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the id is read before the await and the name only once.</b> The card is a
    /// snapshot of one list scan. While the dialog is open the store can change from elsewhere
    /// (another window, a workflow finishing), so after the await the question "does this card
    /// still exist" has to be asked of the store, not of the card. The id is what to ask with; it
    /// is taken before the await because the reference itself may be gone by then.</para>
    /// <para><b>Deleting something that vanished.</b> <c>DeleteToolkit</c> returns false for an id
    /// that is no longer there, which is the right answer for "the reader wanted it gone": they
    /// do not need an error for a wish that has already come true. The refresh runs either way, so
    /// the page shows what the store actually holds.</para>
    /// </remarks>
    private async Task DeleteToolkitAsync(ToolkitCardVM? card)
    {
        if (card is null)
            return;

        var toolkitId = card.Model.GetId();
        var name = card.Model.Meta.Name;

        var confirmed = await _windowService.ConfirmDeleteToolkitAsync(
            name, _windowService.MainWindow);

        // Nothing has been touched at this point. A cancelled or dismissed dialog leaves the
        // store exactly as it was, which is what the test with the recording fake checks.
        if (!confirmed)
            return;

        string? failure = null;

        try
        {
            _toolkitService.DeleteToolkit(toolkitId);
        }
        catch (Exception ex)
        {
            // The store refuses a mounted ToolKit (`ToolkitService.DeleteToolkit` throws
            // `toolkit.mounted`), and moving the directory into `.trash` can fail with an
            // `IOException`. Neither used to reach the screen: the exception travelled to
            // `RxApp.DefaultExceptionHandler`, which writes one log line, so pressing delete
            // looked like nothing happening at all.
            //
            // This is an exit, not a fence. Greying the button out while mounted would hide the
            // failure from anyone who reaches the command from code -- and it would turn an
            // explained refusal into a silent button, which is less than the user has today.
            // `CanDelete` would also be a per-card copy of a service-level fact, i.e. a second
            // place that can go stale; the service is the only thing that knows.
            //
            // The message is written onto the card *after* the rebuild below, and that ordering
            // is the point: `card` is the reference that was captured before the dialog opened,
            // and `_cards` may have been rebuilt while it was up. Writing to the captured
            // instance would put the message on a card the list no longer holds, which looks
            // exactly like the message never being written.
            //
            // The text is the exception's own words: written for developers, not translated.
            // Making it a language key is a separate change -- seven dictionaries and a reason
            // token per code -- and mixing it in here would make the red evidence ambiguous
            // about which half was under test.
            failure = ex.Message;
        }

        // Re-read rather than patch: `_cards` may have been rebuilt while the dialog was up, and
        // editing it here would race whichever handler rebuilt it. This runs whether the delete
        // succeeded or was refused, so the page shows what the store actually holds.
        RefreshCards();

        if (failure is not null)
            _cards.FirstOrDefault(c => c.Model.GetId() == toolkitId)?.ErrorMessage = failure;
    }

    private void RefreshCards()
    {
        _cards.Clear();
        foreach (var toolkit in _toolkitService.ListToolkits())
            _cards.Add(new ToolkitCardVM(toolkit, _toolkitService, _windowService));
        this.RaisePropertyChanged(nameof(IsEmpty));
        this.RaisePropertyChanged(nameof(HasCards));
    }

    /// <summary>Unsubscribes event handlers (D11 page Unloaded-dispose pattern).</summary>
    public void Dispose()
    {
        _toolkitService.ToolkitListChanged -= OnToolkitListChanged;
        _toolkitService.ToolkitEvent -= OnToolkitEventDispatch;
        _eventService.Unsubscribe(EventNames.LanguageChanged, OnLanguageChanged);
    }
}
