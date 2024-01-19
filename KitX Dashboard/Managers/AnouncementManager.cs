using Avalonia.Threading;
using Common.BasicHelper.IO;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Data;
using KitX.Dashboard.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitX.Dashboard.Managers;

internal class AnouncementManager
{




    public static async Task CheckNewAnnouncements()
    {
        var client = new HttpClient();

        client.DefaultRequestHeaders.Accept.Clear();

        //  链接头部
        var linkBase = $"http://" +
            $"{ConfigManager.AppConfig.Web.APIServer}" +
            $"{ConfigManager.AppConfig.Web.APIPath}";

        //  获取公告列表的api链接
        var link = $"{linkBase}{GlobalInfo.Api_Get_Announcements}";

        //  公告列表
        var msg = await client.GetStringAsync(link);

        var list = JsonSerializer.Deserialize<List<string>>(msg);

        //  本地已阅列表
        List<string>? readed;

        var confPath = GlobalInfo.AnnouncementsJsonPath.GetFullPath();

        if (File.Exists(confPath))
            readed = JsonSerializer.Deserialize<List<string>>(
                await FileHelper.ReadAllAsync(confPath)
            );
        else readed = new();

        //  未阅读列表
        var unreads = new List<DateTime>();

        //  添加没有阅读的公告到未阅读列表
        if (list is not null && readed is not null)
        {
            foreach (var item in list)
                if (!readed.Contains(item))
                    unreads.Add(DateTime.Parse(item));

            //  公告列表<发布时间, 公告内容>
            var src = new Dictionary<string, string>();

            foreach (var item in unreads)
            {
                //  获取单个公告的链接
                var apiLink = $"{linkBase}{GlobalInfo.Api_Get_Announcement}" +
                    $"?" +
                    $"lang={ConfigManager.AppConfig.App.AppLanguage}" +
                    $"&" +
                    $"date={item:yyyy-MM-dd HH-mm}";

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
                    var toast = new AnouncementsWindow();

                    toast.UpdateSource(src, readed);
                    toast.Show();
                });
            }
        }

        //  结束Http客户端
        client.Dispose();
    }
}
