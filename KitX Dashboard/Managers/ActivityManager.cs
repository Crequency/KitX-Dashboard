﻿using Common.Activity;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Names;
using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace KitX.Dashboard.Managers;

internal class ActivityManager
{
    private static readonly object _activityRecordLock = new();

    public static string CollectionName => DateTime.UtcNow.ToString("yyyy_MM").Num2UpperChar();

    private static Activity? _appActivity;

    public static List<Activity> ReadActivities()
    {
        if (Instances.ActivitiesDataBase is LiteDatabase db)
        {
            var col = db.GetCollection<Activity>(CollectionName);

            return col.FindAll().ToList();
        }
        else return [];
    }

    public static void Record(Activity activity, Expression<Func<Activity, int>> keySelector)
    {
        var location = $"{nameof(ActivityManager)}.{nameof(Record)}";

        TasksManager.RunTask(() =>
        {
            lock (_activityRecordLock)
            {
                if (Instances.ActivitiesDataBase is LiteDatabase db)
                {
                    var col = db.GetCollection<Activity>(CollectionName);

                    col?.Insert(activity);

                    col?.EnsureIndex(keySelector);

                    ConfigManager.Instance.AppConfig.Activity.TotalRecorded += col is null ? 0 : 1;

                    db.Commit();
                }
            }
        }, location, catchException: true);
    }

    public static void Update(Activity activity)
    {
        var location = $"{nameof(ActivityManager)}.{nameof(Update)}";

        TasksManager.RunTask(() =>
        {
            lock (_activityRecordLock)
            {
                if (Instances.ActivitiesDataBase is LiteDatabase db)
                {
                    var col = db.GetCollection<Activity>(CollectionName);

                    col?.Update(activity);

                    db.Commit();
                }
            }
        }, location, catchException: true);
    }

    public static void RecordAppStart()
    {
        var activity = new Activity()
        {
            Id = ConfigManager.Instance.AppConfig.Activity.TotalRecorded,
            Name = nameof(ActivityNames.AppLifetime),
            Author = ConstantTable.AppFullName,
            Title = ActivityTitles.AppStart,
            Category = nameof(ActivitySortNames.DashboardEvent),
            IconKind = Material.Icons.MaterialIconKind.RocketLaunch,
        }.Open(ConstantTable.AppFullName);

        _appActivity = activity;

        Record(activity, x => x.Id);
    }

    public static void RecordAppExit()
    {
        if (_appActivity is Activity activity)
        {
            activity.Close(ConstantTable.AppFullName);

            Update(activity);
        }
    }
}
