using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Timers;

namespace KitX_Dashboard.Managers;

internal class StatisticsManager
{
    internal static Dictionary<string, double>? UseStatistics = new();

    internal static void Start()
    {
        InitEvents();

        RecoverPreviousStatistics();

        BeginRecord();
    }

    internal static void InitEvents()
    {
        EventService.UseStatisticsChanged += async () =>
        {
            try
            {
                var dataDir = GlobalInfo.DataPath.GetFullPath();
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

                var useFile = "UseCount.json";
                var usePath = $"{dataDir}/{useFile}".GetFullPath();
                var json = JsonSerializer.Serialize(UseStatistics);

                await File.WriteAllTextAsync(usePath, json);

            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"On UseStatisticsChanged: {ex.Message}");
            }
        };
    }

    internal static async void RecoverPreviousStatistics()
    {
        var dataDir = GlobalInfo.DataPath.GetFullPath();
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

        try
        {
            var useFile = "UseCount.json";
            var usePath = $"{dataDir}/{useFile}".GetFullPath();

            if (File.Exists(usePath))
            {
                var useCountJson = await File.ReadAllTextAsync(usePath);

                UseStatistics = JsonSerializer.Deserialize<Dictionary<string, double>>(useCountJson);

                if (UseStatistics is not null)
                {
                    var lastDT = DateTime.Parse(UseStatistics.Keys.Last());
                    while (!lastDT.ToString("MM.dd").Equals(DateTime.Now.ToString("MM.dd")))
                    {
                        lastDT = lastDT.AddDays(1);

                        UseStatistics.Add(lastDT.ToString("MM.dd"), 0);
                    }
                }
            }
            else
            {
                var today = DateTime.Now.ToString("MM.dd");

                UseStatistics?.Add(today, 0);

                var json = JsonSerializer.Serialize(UseStatistics);
                await File.WriteAllTextAsync(usePath, json);
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, e.Message);
        }
    }

    internal static void BeginRecord()
    {
        var location = $"{nameof(StatisticsManager)}.{nameof(BeginRecord)}";

        var use_timer = new Timer()
        {
            Interval = 1000 * 60 * 0.6    //  Update per 0.6 minutes
        };
        use_timer.Elapsed += (_, _) =>
        {
            try
            {
                var today = DateTime.Now.ToString("MM.dd");

                if (UseStatistics is null) return;

                if (UseStatistics.ContainsKey(today))
                {
                    UseStatistics[today] += 0.01;
                    UseStatistics[today] = Math.Round(UseStatistics[today], 2);
                }
                else
                {
                    UseStatistics.Add(today, 0.01);
                }

                EventService.Invoke(nameof(EventService.UseStatisticsChanged));
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        };
        use_timer.Start();
    }
}
