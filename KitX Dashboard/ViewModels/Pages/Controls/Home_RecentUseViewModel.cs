using System.Collections.ObjectModel;
using KitX.Core.Contract.Activity;
using KitX.Core.Plugin;
using KitX.Dashboard.Views;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_RecentUseViewModel : ViewModelBase, IView
{
    private readonly IActivityService _activityService;

    public Home_RecentUseViewModel(IActivityService activityService)
    {
        _activityService = activityService;

        InitCommands();

        InitEvents();

        LoadRecentPlugins();
    }

    /// <summary>
    /// D7: wires the "recently used" list to the activity store. No plugin-launch
    /// activity type is recorded yet (Core currently records AppLifetime only), so
    /// this yields an empty list and the "no recent" tip stays visible; the pipeline
    /// is in place for future plugin-launch recordings. Follows the
    /// Home_ActivityLogViewModel pattern (IActivityService read).
    /// </summary>
    private void LoadRecentPlugins()
    {
        var recent = _activityService.ReadActivities();

        NoRecent_TipHeight = recent.Count == 0 ? 200 : 0;
    }

    public double NoRecent_TipHeight { get; set; } = 200;

    public ObservableCollection<PluginInstallation> RecentPlugins { get; } = [];

    public override void InitCommands() { }

    public override void InitEvents() { }
}
