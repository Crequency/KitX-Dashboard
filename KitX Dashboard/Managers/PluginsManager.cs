using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Data;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Formats.KXP;
using KitX.Web.Rules;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace KitX.Dashboard.Managers;

internal class PluginsManager
{

    internal static List<Plugin> Plugins = new();

    internal static void ImportPlugin(string[] kxpfiles, bool inGraphic = false)
    {
        var location = $"{nameof(PluginsManager)}.{nameof(ImportPlugin)}";

        var processPath = Environment.ProcessPath
            ?? throw new Exception("Can not get path of `KitX Dashboard` process.");

        var workbase = Path.GetDirectoryName(processPath)
            ?? throw new Exception("Can not get work base of `KitX`.");

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

                var pluginsavedir = config?.App?.LocalPluginsFileFolder.GetFullPath();

                var thisplugindir = $"" +
                    $"{pluginsavedir}/" +
                    $"{pluginStruct.PublisherName}_{pluginStruct.AuthorName}/" +
                    $"{pluginStruct.Name}/" +
                    $"{pluginStruct.Version}/";

                thisplugindir = thisplugindir.GetFullPath();

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
                var msg = $"In {location}: Processing {item} occurs error -> {e.Message}";

                Console.WriteLine(msg);

                if (inGraphic)
                {
                    Log.Error(e, msg);

                    throw;  //  如果是图形界面调用, 则再次抛出便于给出图形化提示
                }
            }
        }

        EventService.Invoke(nameof(EventService.PluginsListChanged));
    }
}
