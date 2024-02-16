using Avalonia.Threading;
using Common.BasicHelper.IO;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Views;
using System;
using System.Collections.Generic;
using System.IO;
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

        var appConfig = Instances.ConfigManager.AppConfig;

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

        var readed = new List<string>();

        var confPath = ConstantTable.AnnouncementsJsonPath.GetFullPath();

        if (File.Exists(confPath))
            readed = JsonSerializer.Deserialize<List<string>>(
                await FileHelper.ReadAllAsync(confPath)
            );
        else readed = [];

        var unreads = new List<DateTime>();

        if (list is null || readed is null) return;

        foreach (var item in list)
            if (!readed.Contains(item))
                unreads.Add(DateTime.Parse(item));

        var src = new Dictionary<string, string>();

        foreach (var item in unreads)
        {
            var apiLink = new StringBuilder()
                .Append($"{linkBase}{ConstantTable.Api_Get_Announcement}")
                .Append('?')
                .Append($"lang={Instances.ConfigManager.AppConfig.App.AppLanguage}")
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
                var toast = new AnouncementsWindow().UpdateSource(src, readed);

                toast.Show();
            });
        }
    }
}
