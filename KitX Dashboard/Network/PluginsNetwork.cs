using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Data;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views.Pages.Controls;
using KitX.Shared.Plugin;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace KitX.Dashboard.Managers;

internal class PluginsNetwork
{
    internal static void Execute(string msg, IPEndPoint endPoint)
    {
        var location = $"{nameof(PluginsNetwork)}.{nameof(Execute)}";

        try
        {
            if (msg.StartsWith("PluginInfo: "))
            {
                var json = msg[14..];

                var pluginStruct = JsonSerializer.Deserialize<PluginInfo>(json);

                pluginStruct.Tags ??= [];

                // 标注实例注册 ID
                pluginStruct.Tags.Add("Authorized_ID",
                    $"{pluginStruct.PublisherName}" +
                    $"." +
                    $"{pluginStruct.Name}" +
                    $"." +
                    $"{pluginStruct.Version}"
                );

                // 标注 IPEndPoint
                pluginStruct.Tags.Add("IPEndPoint", endPoint.ToString());

                pluginsToAdd.Enqueue(pluginStruct);

                var workPath = ConfigManager.AppConfig.App.LocalPluginsDataFolder.GetFullPath();
                var sendtxt = $"WorkPath: {workPath}";
                var bytes = sendtxt.FromUTF8();

                Instances.WebManager?.pluginsServer?.Send(bytes, endPoint.ToString());
            }
        }
        catch (Exception e)
        {
            Log.Error(e, $"In {location}: (msg) => msg: {msg}; {e.Message}");
        }
    }

    internal static readonly Queue<IPEndPoint> pluginsToRemove = new();

    internal static readonly Queue<PluginInfo> pluginsToAdd = new();

    internal static readonly Queue<Plugin> pluginsToRemoveFromDB = new();

    internal static readonly Queue<Plugin> pluginsToDelete = new();

    internal static readonly object PluginsListOperationLock = new();

    internal static void KeepCheckAndRemove()
    {
        var location = $"{nameof(PluginsNetwork)}.{nameof(KeepCheckAndRemove)}";

        bool timerElapsing = false;

        var timer = new System.Timers.Timer()
        {
            Interval = 10,
            AutoReset = true
        };

        timer.Elapsed += (_, _) =>
        {
            if (timerElapsing)
                return;
            else
                timerElapsing = true;

            try
            {
                if (pluginsToAdd.Count > 0)
                {
                    var pluginStruct = pluginsToAdd.Dequeue();

                    Dispatcher.UIThread.Post(() =>
                    {
                        var card = new PluginCard(pluginStruct)
                        {
                            IPEndPoint = pluginStruct.Tags["IPEndPoint"]
                        };

                        Instances.PluginCards.Add(card);
                    });
                }

                if (pluginsToRemove.Count > 0)
                {
                    var endPoint = pluginsToRemove.Dequeue().ToString();

                    var matched = Instances.PluginCards.FirstOrDefault(
                        x => x!.IPEndPoint?.Equals(endPoint) ?? false,
                        null
                    );

                    if (matched is not null)
                        Instances.PluginCards.Remove(matched);
                }

                if (!GlobalInfo.Running)
                {
                    timer.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
            }

            timerElapsing = false;
        };

        timer.Start();
    }

    internal static void Disconnect(IPEndPoint endPoint)
    {
        pluginsToRemove.Enqueue(endPoint);
    }

    internal static void RequireRemovePlugin(Plugin plugin) => pluginsToRemoveFromDB.Enqueue(plugin);

    internal static void RequireDeletePlugin(Plugin plugin) => pluginsToDelete.Enqueue(plugin);

    internal static void KeepCheckAndRemoveOrDelete()
    {
        var location = $"{nameof(PluginsNetwork)}.{nameof(KeepCheckAndRemoveOrDelete)}";

        System.Timers.Timer timer = new()
        {
            Interval = 2000,
            AutoReset = true
        };

        timer.Elapsed += (_, _) =>
        {
            try
            {
                var isPluginsListUpdated = false;

                if (pluginsToRemoveFromDB.Count > 0)
                {
                    isPluginsListUpdated = true;

                    while (pluginsToRemoveFromDB.Count > 0)
                    {
                        var plugin = pluginsToRemoveFromDB.Dequeue();

                        lock (PluginsListOperationLock)
                        {
                            PluginsManager.Plugins.RemoveAt(
PluginsManager.Plugins.FindIndex(
                                    x =>
                                    {
                                        if (x.InstallPath is null) return false;

                                        return x.InstallPath.Equals(plugin.InstallPath);
                                    }
                                )
                            );
                        }
                    }
                }

                if (pluginsToDelete.Count > 0)
                {
                    isPluginsListUpdated = true;

                    while (pluginsToDelete.Count > 0)
                    {
                        var plugin = pluginsToDelete.Dequeue();

                        lock (PluginsListOperationLock)
                        {
                            PluginsManager.Plugins.RemoveAt(
PluginsManager.Plugins.FindIndex(
                                    x =>
                                    {
                                        if (x.InstallPath is not null)
                                            return x.InstallPath.Equals(plugin.InstallPath);
                                        else return false;
                                    }
                                )
                            );
                        }

                        var pgfiledir = Path.GetFullPath(
                            $"{ConfigManager.AppConfig.App.LocalPluginsFileFolder}/" +
                            $"{plugin.PluginDetails.PublisherName}_{plugin.PluginDetails.AuthorName}/" +
                            $"{plugin.PluginDetails.Name}/{plugin.PluginDetails.Version}/"
                        );

                        Directory.Delete(pgfiledir, true);
                    }
                }

                if (isPluginsListUpdated) EventService.Invoke(nameof(EventService.PluginsListChanged));
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        };

        timer.Start();
    }
}
