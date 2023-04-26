using Common.BasicHelper.Utils.Extensions;
using KitX.KXP.Helper;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using KitX_Dashboard.Services;
using System;
using System.Collections.Generic;
using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace KitX_Dashboard.Managers;

internal class PluginsManager
{

    internal static List<Plugin> Plugins = new();

    /// <summary>
    /// 导入插件
    /// </summary>
    /// <param name="kxpfiles">.kxp files list</param>
    internal static void ImportPlugin(string[] kxpfiles, bool inGraphic = false)
    {
        var workbasef = Environment.ProcessPath
            ?? throw new Exception("Can not get path of `KitX Dashboard.exe`");

        var workbase = Path.GetDirectoryName(workbasef)
            ?? throw new Exception("Can not get work base of `KitX`");

        foreach (var item in kxpfiles)
        {
            try
            {
                var decoder = new Decoder(item);
                var rst = decoder.GetLoaderAndPluginStruct();
                var loaderStruct = JsonSerializer.Deserialize<LoaderStruct>(rst.Item1);
                var pluginStruct = JsonSerializer.Deserialize<PluginStruct>(rst.Item2);

                var config = inGraphic ?
                    ConfigManager.AppConfig :
                    JsonSerializer.Deserialize<AppConfig>(
                        File.ReadAllText(GlobalInfo.ConfigFilePath)
                    );

                if (config is null)
                {
                    Console.WriteLine($"No config file found!");

                    if (!inGraphic) Environment.Exit(ErrorCodes.ConfigFileDidntExists);
                }

                var pluginsavedir = config?.App?.LocalPluginsFileFolder;

                if (pluginsavedir is not null)
                    pluginsavedir = pluginsavedir.GetFullPath();

                var thisplugindir = $"" +
                    $"{pluginsavedir}/" +
                    $"{pluginStruct.PublisherName}_{pluginStruct.AuthorName}/" +
                    $"{pluginStruct.Name}/" +
                    $"{pluginStruct.Version}/"
                    .GetFullPath();

                if (Directory.Exists(thisplugindir))
                    Directory.Delete(thisplugindir, true);
                _ = Directory.CreateDirectory(thisplugindir);

                _ = decoder.Decode(thisplugindir);

                if (!Plugins.Exists(x => x.InstallPath?.Equals(thisplugindir) ?? false))
                    Plugins.Add(new()
                    {
                        InstallPath = thisplugindir
                    });
            }
            catch (Exception e)
            {
                Console.WriteLine($"Processing {item} occurs error: {e.Message}");

                if (inGraphic) throw;       //  如果是图形界面调用, 则再次抛出便于给出图形化提示
            }
        }

        EventService.Invoke(nameof(EventService.PluginsListChanged));
    }
}
