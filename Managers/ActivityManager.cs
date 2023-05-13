using Common.Activity;
using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Data;
using KitX_Dashboard.Names;
using LiteDB;
using System;
using System.Linq.Expressions;

namespace KitX_Dashboard.Managers;

internal class ActivityManager
{
    private static readonly object _activityRecordLock = new();

    public static string CollectionName => DateTime.UtcNow.ToString("yyyy_MM").Num2UpperChar();

    private static Activity? _appActivity;

    /// <summary>
    /// 记录活动
    /// </summary>
    /// <param name="activity">活动</param>
    /// <param name="keySelector">键选择器</param>
    public static void Record(Activity activity, Expression<Func<Activity, int>> keySelector)
    {
        lock (_activityRecordLock)
        {
            if (Program.ActivitiesDataBase is LiteDatabase db)
            {
                var col = db.GetCollection<Activity>(CollectionName);

                col?.Insert(activity);

                col?.EnsureIndex(keySelector);

                ConfigManager.AppConfig.Activity.TotalRecorded += col is null ? 0 : 1;

                db.Commit();
            }
        }
    }

    /// <summary>
    /// 更新活动
    /// </summary>
    /// <param name="activity">活动</param>
    public static void Update(Activity activity)
    {
        lock (_activityRecordLock)
        {
            if (Program.ActivitiesDataBase is LiteDatabase db)
            {
                var col = db.GetCollection<Activity>(CollectionName);

                col?.Update(activity);

                db.Commit();
            }
        }
    }

    public static void RecordAppStart()
    {
        var activity = new Activity()
        {
            Id = ConfigManager.AppConfig.Activity.TotalRecorded + 1,
            Name = nameof(ActivityNames.AppLifetime),
            Author = GlobalInfo.AppFullName,
            Title = ActivityTitles.AppStart,
            Category = nameof(ActivitySortNames.DashboardEvent),
            IconKind = Material.Icons.MaterialIconKind.RocketLaunch
        }
        .Open(GlobalInfo.AppFullName)
        ;

        _appActivity = activity;

        Record(activity, x => x.Id);
    }

    public static void RecordAppExit()
    {
        if (_appActivity is Activity activity)
        {
            activity.Close(GlobalInfo.AppFullName);

            Update(activity);
        }
    }
}
