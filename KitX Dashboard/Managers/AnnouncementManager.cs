using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using KitX.Dashboard.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;

namespace KitX.Dashboard.Managers;

internal class AnnouncementManager
{
    public static async Task CheckNewAnnouncements()
    {
        const string location = $"{nameof(AnnouncementsWindow)}.{nameof(CheckNewAnnouncements)}";

        var appConfig = ConfigManager.Instance.AppConfig;

        var linkBase = new StringBuilder().Append("https://").Append(appConfig.Web.ApiServer).Append(appConfig.Web.ApiPath).ToString();

        var link = new StringBuilder().Append(linkBase).Append(ConstantTable.ApiGetAnnouncements).ToString();

        try
        {
            using var client = new HttpClient();

            client.DefaultRequestHeaders.Accept.Clear();

            var msg = await client.GetStringAsync(link);

            var list = JsonSerializer.Deserialize<List<string>>(msg);

            var accepted = ConfigManager.Instance.AnnouncementConfig.Accepted;

            if (list is null)
                return;

            var unreads = (from item in list where !accepted.Contains(item) select DateTime.Parse(item)).ToList();

            var src = new Dictionary<string, string>();

            foreach (var item in unreads)
            {
                var apiLink = new StringBuilder()
                    .Append($"{linkBase}{ConstantTable.ApiGetAnnouncement}")
                    .Append('?')
                    .Append($"lang={ConfigManager.Instance.AppConfig.App.AppLanguage}")
                    .Append('&')
                    .Append($"date={item:yyyy-MM-dd HH-mm}")
                    .ToString();

                var md = JsonSerializer.Deserialize<string>(await client.GetStringAsync(apiLink));

                if (md is not null)
                    src.Add(item.ToString("yyyy-MM-dd HH:mm"), md);
            }

            if (unreads.Count > 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var toast = new AnnouncementsWindow().UpdateSource(src);

                    ViewInstances.ShowWindow(toast);
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");

            Dispatcher.UIThread.Post(() =>
            {
                var content = new StringBuilder().AppendLine($"GET: {link}").AppendLine().AppendLine(ex.StackTrace).ToString();

                var box = MessageBoxManager.GetMessageBoxStandard(ex.Message, content, icon: Icon.Error).ShowWindowAsync();
            });
        }
    }
}
