using System.Linq;
using Common.BasicHelper.Core.TaskSystem;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using LiteDB;

namespace KitX.Dashboard;

public static class Instances
{
    public static SignalTasksManager? SignalTasksManager { get; set; }

    public static WebManager? WebManager { get; set; }

    public static FileWatcherManager? FileWatcherManager { get; set; }

    public static LiteDatabase? ActivitiesDataBase { get; set; }

    public static KeyHookManager? KeyHookManager { get; set; }

    internal static void Initialize()
    {
        var location = $"{nameof(Instances)}.{nameof(Initialize)}";

        TasksManager.RunTask(() =>
        {
            TasksManager.RunTask(
                () => SignalTasksManager = new(),
                location.Append("." + nameof(SignalTasksManager)),
                catchException: true
            );

            TasksManager.RunTask(
                () => KeyHookManager = new KeyHookManager().Hook(),
                location.Append("." + nameof(KeyHookManager)),
                catchException: true
            );

            TasksManager.RunTask(() =>
            {
                if (ConstantTable.EnabledConfigFileHotReload)
                    FileWatcherManager = new();
            }, location.Append("." + nameof(FileWatcherManager)), catchException: true);
        }, location, catchException: true);
    }
}
