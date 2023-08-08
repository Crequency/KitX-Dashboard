using Common.BasicHelper.Core.TaskSystem;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using LiteDB;
using System.Collections.ObjectModel;

internal class Instances
{
    internal static SignalTasksManager? SignalTasksManager;

    internal static WebManager? WebManager;

    internal static FileWatcherManager? FileWatcherManager;

    internal static ObservableCollection<PluginCard> PluginCards = new();

    internal static ObservableCollection<DeviceCard> DeviceCards = new();

    internal static LiteDatabase? ActivitiesDataBase;

    internal static CacheManager? CacheManager;

    internal static HotKeyManager? HotKeyManager;

    internal static MainWindow? MainWindow;

    internal static PluginsLaunchWindow? PluginsLaunchWindow;
}
