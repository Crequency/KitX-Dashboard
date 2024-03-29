﻿using Avalonia.Threading;
using KitX.Dashboard.Views;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitX.Dashboard.Managers;

internal class AnouncementManager
{
    public static async Task CheckNewAnnouncements()
    {
        using var client = new HttpClient();

        client.DefaultRequestHeaders.Accept.Clear();

        var appConfig = ConfigManager.Instance.AppConfig;

        var linkBase = new StringBuilder()
            .Append("https://")
            .Append(appConfig.Web.ApiServer)
            .Append(appConfig.Web.ApiPath)
            .ToString()
            ;

        var link = new StringBuilder()
            .Append(linkBase)
            .Append(ConstantTable.Api_Get_Announcements)
            .ToString()
            ;

        var msg = await client.GetStringAsync(link);

        var list = JsonSerializer.Deserialize<List<string>>(msg);

        var accepted = ConfigManager.Instance.AnnouncementConfig.Accepted;

        var unreads = new List<DateTime>();

        if (list is null || accepted is null) return;

        foreach (var item in list)
            if (!accepted.Contains(item))
                unreads.Add(DateTime.Parse(item));

        var src = new Dictionary<string, string>();

        foreach (var item in unreads)
        {
            var apiLink = new StringBuilder()
                .Append($"{linkBase}{ConstantTable.Api_Get_Announcement}")
                .Append('?')
                .Append($"lang={ConfigManager.Instance.AppConfig.App.AppLanguage}")
                .Append('&')
                .Append($"date={item:yyyy-MM-dd HH-mm}")
                .ToString()
                ;

            var md = JsonSerializer.Deserialize<string>(
                await client.GetStringAsync(apiLink)
            );

            if (md is not null)
                src.Add(item.ToString("yyyy-MM-dd HH:mm"), md);
        }

        if (unreads.Count > 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var toast = new AnouncementsWindow().UpdateSource(src);

                ViewInstances.ShowWindow(toast);
            });
        }
    }
}
