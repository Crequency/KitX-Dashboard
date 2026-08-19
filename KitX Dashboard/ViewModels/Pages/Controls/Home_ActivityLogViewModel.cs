using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using Common.Activity;
using KitX.Core.Contract.Activity;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_ActivityLogViewModel : ViewModelBase, IDisposable
{
    internal static ObservableCollection<Activity> Activities { get; set; } = [];

    /// <summary>
    /// G6: number of activities fetched per page. The Home card loads only the most recent
    /// <see cref="PageSize"/> rows (reverse-chronological page via <see cref="IActivityService"/>)
    /// instead of the full table scan, and older rows are paged in via "load more".
    /// </summary>
    private const int PageSize = 100;

    private readonly IActivityService _activityService;

    /// <summary>Named handler so <see cref="Dispose"/> can unsubscribe it (D11).</summary>
    private readonly NotifyCollectionChangedEventHandler _activitiesChangedHandler;

    private double noActivityLog_TipHeight = Activities.Count == 0 ? 200 : 0;

    internal double NoActivityLog_TipHeight
    {
        get => noActivityLog_TipHeight;
        set
        {
            noActivityLog_TipHeight = value;

            this.RaisePropertyChanged(nameof(NoActivityLog_TipHeight));
        }
    }

    private bool hasMoreActivities;

    /// <summary>True while the store holds rows the UI has not yet loaded ("load more" shown).</summary>
    internal bool HasMoreActivities
    {
        get => hasMoreActivities;
        private set => this.RaiseAndSetIfChanged(ref hasMoreActivities, value);
    }

    internal ReactiveCommand<Unit, Unit>? LoadMoreCommand { get; private set; }

    public Home_ActivityLogViewModel(IActivityService activityService)
    {
        _activityService = activityService;

        _activitiesChangedHandler = (_, _) =>
        {
            NoActivityLog_TipHeight = Activities.Count == 0 ? 200 : 0;
            HasMoreActivities = LoadMoreAvailable();
        };

        InitCommands();

        InitEvents();

        Activities.Clear();

        // D10 note: IActivityService.GetActivities() returns the adapted IActivity shape
        // (Id/Type/Timestamp/Details), while the XAML binds the rich Common.Activity.Activity
        // fields (Status/Title/Tasks/...). Migrating here would break every binding in
        // Home_ActivityLog.axaml — so we read via the interface's rich ReadActivities() instead.
        LoadMoreActivities();
    }

    public sealed override void InitCommands()
    {
        LoadMoreCommand = ReactiveCommand.Create(LoadMore, this.WhenAnyValue(x => x.HasMoreActivities));
    }

    public sealed override void InitEvents()
    {
        Activities.CollectionChanged += _activitiesChangedHandler;
    }

    /// <summary>
    /// Unsubscribes every subscription made in <see cref="InitEvents"/>. The page is
    /// recreated on each Home navigation (D11).
    /// </summary>
    public void Dispose()
    {
        Activities.CollectionChanged -= _activitiesChangedHandler;
    }

    private bool LoadMoreAvailable() => Activities.Count < _activityService.CountActivities();

    private void LoadMore() => LoadMoreActivities();

    /// <summary>
    /// Loads the next reverse-chronological page (skip = already-loaded count) and prepends it
    /// so the list stays oldest-first / newest-at-bottom, matching the pre-G6 display order.
    /// The newest page is fetched newest-first by <see cref="IActivityService.ReadActivities(int,int)"/>;
    /// inserting each row at index 0 flips it into the oldest-first display order.
    /// </summary>
    private void LoadMoreActivities()
    {
        var older = _activityService.ReadActivities(PageSize, Activities.Count);
        for (var i = 0; i < older.Count; i++)
            Activities.Insert(0, older[i]);

        NoActivityLog_TipHeight = Activities.Count == 0 ? 200 : 0;
        HasMoreActivities = LoadMoreAvailable();
    }
}
