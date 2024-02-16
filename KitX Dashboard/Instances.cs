using Common.BasicHelper.Core.TaskSystem;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard;
using KitX.Dashboard.Managers;
using LiteDB;
using System.Linq;

internal static class Instances
{
    internal static SignalTasksManager? SignalTasksManager { get; set; }

    internal static WebManager? WebManager { get; set; }

    internal static FileWatcherManager? FileWatcherManager { get; set; }

    internal static LiteDatabase? ActivitiesDataBase { get; set; }

    internal static CacheManager? CacheManager { get; set; }

    internal static HotKeyManager? HotKeyManager { get; set; }

    internal static ConfigManager ConfigManager { get; set; } = new ConfigManager().SetLocation("./Config/").Load();

    internal static void Initialize()
    {
        var location = $"{nameof(Instances)}.{nameof(Initialize)}";

        TasksManager.RunTask(() =>
        {
            TasksManager.RunTask(
                () => SignalTasksManager = new(),
                location.Append(nameof(SignalTasksManager)),
                catchException: true
            );

            TasksManager.RunTask(
                () => CacheManager = new(),
                location.Append(nameof(CacheManager)),
                catchException: true
            );

            //TasksManager.RunTask(
            //    () => HotKeyManager = new HotKeyManager().Hook(),
            //    location.Append(nameof(HotKeyManager)),
            //    catchException: true
            //);

            TasksManager.RunTask(() =>
            {
                if (ConstantTable.EnabledConfigFileHotReload)
                    FileWatcherManager = new();
            }, location.Append(nameof(FileWatcherManager)), catchException: true);
        }, location, catchException: true);
    }
}
