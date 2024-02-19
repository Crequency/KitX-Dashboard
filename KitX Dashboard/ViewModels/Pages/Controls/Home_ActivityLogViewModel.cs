using Common.Activity;
using KitX.Dashboard.Managers;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_ActivityLogViewModel : ViewModelBase, INotifyPropertyChanged
{

    public new event PropertyChangedEventHandler? PropertyChanged;

    internal static ObservableCollection<Activity> Activities { get; set; } = [];

    private double noActivityLog_TipHeight = Activities.Count == 0 ? 200 : 0;

    internal double NoActivityLog_TipHeight
    {
        get => noActivityLog_TipHeight;
        set
        {
            noActivityLog_TipHeight = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(NoActivityLog_TipHeight))
            );
        }
    }

    public Home_ActivityLogViewModel()
    {
        InitCommands();

        InitEvents();

        Activities.Clear();

        foreach (var item in ActivityManager.ReadActivities())
            Activities.Add(item);
    }

    public override void InitCommands()
    {

    }

    public override void InitEvents()
    {
        Activities.CollectionChanged += (_, _) =>
        {
            NoActivityLog_TipHeight = Activities.Count == 0 ? 200 : 0;
        };
    }
}
