using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Plugin;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class LibPageViewModel : ViewModelBase, IDisposable
{
    /// <summary>Named handler so <see cref="Dispose"/> can unsubscribe it (D11).</summary>
    private readonly NotifyCollectionChangedEventHandler _pluginInfosChangedHandler;

    public LibPageViewModel()
    {
        _pluginInfosChangedHandler = (_, e) =>
        {
            // D-REG: plugin events may arrive on the server thread — the filter
            // mutates UI-bound collections, so marshal it to the UI thread.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RecordChange(e);
                ScheduleFlush();
            });
        };

        InitCommands();

        // D-REG: plugins may already be connected before this page is navigated to —
        // the filtered view must be populated from the current source (CollectionChanged
        // alone only fires on future changes, leaving the page empty on first open).
        // InitEvents performs that initial ApplyFilter (and the same recompute on every
        // re-attach, covering plugins connected while the page was unloaded).
        InitEvents();
    }

    public sealed override void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create<PluginInfo>(info =>
        {
            if (UIStateService.MainWindow is not null)
                new PluginDetailWindow() { WindowStartupLocation = WindowStartupLocation.CenterOwner }
                    .SetPluginInfo(info)
                    .Show(UIStateService.MainWindow);
        });

        // G4: grows the realized window of plugin cards (rendering cost, not the filter).
        LoadMoreCommand = ReactiveCommand.Create(LoadMore);
    }

    /// <summary>Guard so <see cref="InitEvents"/> is idempotent — the page re-invokes
    /// it on every Loaded (D11 symmetry), and a duplicate subscription would run the
    /// handler (and its UI-thread post) once per extra subscribe.</summary>
    private bool _eventsSubscribed;

    public sealed override void InitEvents()
    {
        if (_eventsSubscribed) return;
        _eventsSubscribed = true;
        PluginInfos.CollectionChanged += _pluginInfosChangedHandler;
        // Re-attach path: the page was unloaded (Dispose unsubscribed) and plugins may
        // have connected meanwhile — those events are invisible to the incremental
        // path, so recompute from the authoritative source or the page stays frozen
        // at its detach-time values forever.
        ApplyFilter();
    }

    /// <summary>
    /// Unsubscribes every subscription made in <see cref="InitEvents"/> (D11).
    /// </summary>
    public void Dispose()
    {
        if (!_eventsSubscribed) return;
        _eventsSubscribed = false;
        PluginInfos.CollectionChanged -= _pluginInfosChangedHandler;
    }

    private string? _searchingText;

    public string? SearchingText
    {
        get => _searchingText;
        set
        {
            if (_searchingText == value) return;
            _searchingText = value;
            // Keyword changes rebuild the whole filtered view (the only full-recompute path);
            // live Add/Remove events are reconciled incrementally instead (G4).
            ApplyFilter();
        }
    }

    /// <summary>
    /// Filtered view of <see cref="PluginInfos"/> bound by the page's plugin grid.
    /// Matches plugin name / author / any localized display name, ignoring case;
    /// an empty keyword shows all.
    /// </summary>
    private readonly ObservableCollection<PluginInfo> _displayedPluginInfos = [];

    public ObservableCollection<PluginInfo> DisplayedPluginInfos => _displayedPluginInfos;

    // ── G4: incremental filter + coalesced flush + bounded rendering window ──
    // The old ApplyFilter cleared and re-added every plugin on each CollectionChanged, so a
    // 300-plugin startup storm did 300 x O(300) rebuilds. Now source Add/Remove events are
    // accumulated and flushed through an incremental reconciliation (no full Clear), and the
    // flush is coalesced so a storm merges into one recompute. Because Avalonia 11.3 has no
    // virtualizing wrap/grid panel (no ItemsRepeater/UniformGridLayout), the grid also caps the
    // number of concurrently realized cards to a window; "load more" grows it on demand.

    /// <summary>Full filtered result, in source order. <see cref="DisplayedPluginInfos"/> is
    /// the first <see cref="_visibleCount"/> of this list (rendered window).</summary>
    private readonly List<PluginInfo> _filteredPlugins = [];

    /// <summary>How many of <see cref="_filteredPlugins"/> are currently realized.</summary>
    private int _visibleCount;

    private const int InitialBatchSize = 150;
    private const int PageBatchSize = 150;

    private readonly List<PluginInfo> _pendingAdds = [];
    private readonly List<PluginInfo> _pendingRemoves = [];
    private bool _pendingReset;
    private bool _flushScheduled;

    private void RecordChange(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                    foreach (PluginInfo? item in e.NewItems)
                        if (item is not null) AddPending(item);
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                    foreach (PluginInfo? item in e.OldItems)
                        if (item is not null) RemovePending(item);
                break;
            case NotifyCollectionChangedAction.Replace:
                if (e.OldItems is not null)
                    foreach (PluginInfo? item in e.OldItems)
                        if (item is not null) RemovePending(item);
                if (e.NewItems is not null)
                    foreach (PluginInfo? item in e.NewItems)
                        if (item is not null) AddPending(item);
                break;
            case NotifyCollectionChangedAction.Reset:
                // Source cleared/rebuilt — a full recompute reads the current source directly.
                _pendingReset = true;
                _pendingAdds.Clear();
                _pendingRemoves.Clear();
                break;
            case NotifyCollectionChangedAction.Move:
                break; // membership unchanged; a reorder does not affect filtering
        }
    }

    private void AddPending(PluginInfo item)
    {
        if (_pendingRemoves.Remove(item)) return; // was removed, now re-added → cancel out
        if (!_pendingAdds.Contains(item)) _pendingAdds.Add(item);
    }

    private void RemovePending(PluginInfo item)
    {
        if (_pendingAdds.Remove(item)) return; // was added, now removed → cancel out
        if (!_pendingRemoves.Contains(item)) _pendingRemoves.Add(item);
    }

    /// <summary>
    /// Coalesces the storm: the first change in a batch schedules the flush; later changes in
    /// the same UI-pump drain just accumulate, so N rapid events collapse into one reconciliation.
    /// </summary>
    private void ScheduleFlush()
    {
        if (_flushScheduled) return;
        _flushScheduled = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(FlushPending);
    }

    private void FlushPending()
    {
        _flushScheduled = false;

        if (_pendingReset)
        {
            _pendingReset = false;
            _pendingAdds.Clear();
            _pendingRemoves.Clear();
            ApplyFilter();
            return;
        }

        if (_pendingAdds.Count == 0 && _pendingRemoves.Count == 0)
            return;

        foreach (var removed in _pendingRemoves)
        {
            _filteredPlugins.Remove(removed);
            _displayedPluginInfos.Remove(removed);
        }

        foreach (var added in _pendingAdds)
        {
            if (!MatchesFilter(added) || _filteredPlugins.Contains(added))
                continue;
            _filteredPlugins.Add(added);
        }

        _pendingAdds.Clear();
        _pendingRemoves.Clear();

        RefillWindow();
        UpdateCountAndTip();
    }

    private bool MatchesFilter(PluginInfo plugin)
    {
        var keyword = _searchingText?.Trim() ?? string.Empty;
        if (keyword.Length == 0)
            return true;
        return plugin.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || plugin.AuthorName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || plugin.DisplayName.Values.Any(v => v.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFilter()
    {
        _filteredPlugins.Clear();

        foreach (var plugin in PluginInfos)
            if (MatchesFilter(plugin))
                _filteredPlugins.Add(plugin);

        _visibleCount = Math.Min(InitialBatchSize, _filteredPlugins.Count);

        _displayedPluginInfos.Clear();
        RefillWindow();

        // Discard any deferred work — ApplyFilter already read the authoritative source.
        _pendingAdds.Clear();
        _pendingRemoves.Clear();
        _pendingReset = false;
        _flushScheduled = false;

        UpdateCountAndTip();
    }

    /// <summary>Keeps <see cref="DisplayedPluginInfos"/> equal to the first
    /// <c>min(_visibleCount, _filteredPlugins.Count)</c> entries (incremental tail appends).
    /// The window grows into the initial batch as plugins connect (so a page that starts
    /// empty fills up correctly) and only expands beyond it via <see cref="LoadMore"/>;
    /// it never shrinks on a re-fill.</summary>
    private void RefillWindow()
    {
        _visibleCount = Math.Max(_visibleCount, Math.Min(InitialBatchSize, _filteredPlugins.Count));
        while (_displayedPluginInfos.Count < _visibleCount
               && _displayedPluginInfos.Count < _filteredPlugins.Count)
            _displayedPluginInfos.Add(_filteredPlugins[_displayedPluginInfos.Count]);
    }

    private void LoadMore()
    {
        if (_visibleCount >= _filteredPlugins.Count) return;
        _visibleCount = Math.Min(_filteredPlugins.Count, _visibleCount + PageBatchSize);
        RefillWindow();
        UpdateCountAndTip();
    }

    private bool hasMoreItems;

    /// <summary>True while the filtered list has more cards than the realized window.</summary>
    public bool HasMoreItems
    {
        get => hasMoreItems;
        private set => this.RaiseAndSetIfChanged(ref hasMoreItems, value);
    }

    internal ReactiveCommand<Unit, Unit>? LoadMoreCommand { get; private set; }

    private void UpdateCountAndTip()
    {
        PluginsCount = $"{PluginInfos.Count}";
        NoPlugins_TipHeight = _filteredPlugins.Count == 0 ? 300 : 0;
        HasMoreItems = _visibleCount < _filteredPlugins.Count;
    }

    public string pluginsCount = $"{PluginInfos.Count}";

    public string PluginsCount
    {
        get => pluginsCount;
        set => this.RaiseAndSetIfChanged(ref pluginsCount, value);
    }

    public double noPlugins_tipHeight = PluginInfos.Count == 0 ? 300 : 0;

    public double NoPlugins_TipHeight
    {
        get => noPlugins_tipHeight;
        set => this.RaiseAndSetIfChanged(ref noPlugins_tipHeight, value);
    }

    public static ObservableCollection<PluginInfo> PluginInfos => UIStateService.PluginInfos;

    internal ReactiveCommand<PluginInfo, Unit>? ViewDetailsCommand { get; set; }
}
