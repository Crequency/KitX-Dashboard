using System.Collections.ObjectModel;
using KitX.Core.Activity;
using KitX.Core.Plugin;
using KitX.Dashboard.Views;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_RecentUseViewModel : ViewModelBase, IView
{
    public Home_RecentUseViewModel()
    {
        InitCommands();

        InitEvents();

        LoadRecentPlugins();
    }

    /// <summary>
    /// D7: wires the "recently used" list to the activity store. No plugin-launch
    /// activity type is recorded yet (Core currently records AppLifetime only), so
    /// this yields an empty list and the "no recent" tip stays visible; the pipeline
    /// is in place for future plugin-launch recordings. Follows the
    /// Home_ActivityLogViewModel pattern (static ActivityManager read).
    /// </summary>
    private void LoadRecentPlugins()
    {
        var recent = ActivityManager.ReadActivities();

        NoRecent_TipHeight = recent.Count == 0 ? 200 : 0;
    }

    public double NoRecent_TipHeight { get; set; } = 200;

    public ObservableCollection<PluginInstallation> RecentPlugins { get; } = [];

    public override void InitCommands() { }

    public override void InitEvents() { }
}
