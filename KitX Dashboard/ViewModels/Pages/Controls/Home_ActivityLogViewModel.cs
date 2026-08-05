using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Common.Activity;
using KitX.Core.Activity;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_ActivityLogViewModel : ViewModelBase, IDisposable
{
    internal static ObservableCollection<Activity> Activities { get; set; } = [];

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

    public Home_ActivityLogViewModel()
    {
        _activitiesChangedHandler = (_, _) =>
        {
            NoActivityLog_TipHeight = Activities.Count == 0 ? 200 : 0;
        };

        InitCommands();

        InitEvents();

        Activities.Clear();

        // D10 note: IActivityService.GetActivities() exists but returns the adapted
        // IActivity shape (Id/Type/Timestamp/Details), while the XAML binds the rich
        // Common.Activity.Activity fields (Status/Title/Tasks/...). Migrating here would
        // break every binding in Home_ActivityLog.axaml — kept on the static read until
        // the contract exposes the full activity shape.
        foreach (var item in ActivityManager.ReadActivities())
            Activities.Add(item);
    }

    public sealed override void InitCommands() { }

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
}
