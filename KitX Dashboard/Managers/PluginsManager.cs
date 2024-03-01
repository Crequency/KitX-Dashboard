using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;
using Serilog;
using Decoder = KitX.FileFormats.CSharp.ExtensionsPackage.Decoder;

namespace KitX.Dashboard.Managers;

internal class PluginsManager
{
    internal static List<PluginInstallation> Plugins => ConfigManager.Instance.PluginsConfig.Plugins;

    internal static void ImportPlugin(string[] kxpfiles, bool inGraphic = false)
    {
        var location = $"{nameof(PluginsManager)}.{nameof(ImportPlugin)}";

        var processPath = Environment.ProcessPath ?? throw new Exception("Can not get path of `KitX.Dashboard` process.");

        var workbase = Path.GetDirectoryName(processPath) ?? throw new Exception("Can not get work base of `KitX`.");

        foreach (var item in kxpfiles)
        {
            try
            {
                var decoder = new Decoder(item);

                var rst = decoder.GetLoaderAndPluginInfo();

                var loaderInfo = JsonSerializer.Deserialize<LoaderInfo>(rst.Item1);

                var pluginInfo = JsonSerializer.Deserialize<PluginInfo>(rst.Item2);

                if (pluginInfo is null) continue;

                var config = ConfigManager.Instance.AppConfig;

                var pluginsavedir = config?.App?.LocalPluginsFileFolder.GetFullPath();

                var thisPluginDir = new StringBuilder()
                    .Append(pluginsavedir)
                    .Append('/')
                    .Append($"{pluginInfo.PublisherName}_{pluginInfo.AuthorName}")
                    .Append('/')
                    .Append(pluginInfo.Name)
                    .Append('/')
                    .Append(pluginInfo.Version)
                    .ToString()
                    .GetFullPath()
                    ;

                if (Directory.Exists(thisPluginDir))
                    Directory.Delete(thisPluginDir, true);

                _ = Directory.CreateDirectory(thisPluginDir);

                _ = decoder.Decode(thisPluginDir);

                if (!Plugins.Exists(x => x.InstallPath?.Equals(thisPluginDir) ?? false))
                    Plugins.Add(new()
                    {
                        InstallPath = thisPluginDir
                    });
            }
            catch (Exception e)
            {
                var msg = $"In {location}: Processing {item} occurs error -> {e.Message}";

                Console.WriteLine(msg);

                if (inGraphic)
                {
                    Log.Error(e, msg);

                    throw;  // If called in graphic mode, throw again for better tip
                }
            }
        }

        EventService.Invoke(nameof(EventService.PluginsConfigChanged));
    }
}
