using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views.Pages.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;

namespace KitX_Dashboard.Managers;

internal class PluginsNetwork
{
    /// <summary>
    /// 执行 Socket 消息
    /// </summary>
    /// <param name="msg">消息</param>
    internal static void Execute(string msg, IPEndPoint endPoint)
    {
        var location = $"{nameof(PluginsNetwork)}.{nameof(Execute)}";

        try
        {
            var pluginStruct = JsonSerializer.Deserialize<PluginStruct>(msg);

            pluginStruct.Tags ??= new();

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
        }
        catch (Exception e)
        {
            Log.Error(e, $"In {location}: (msg) => msg: {msg}; {e.Message}");
        }
    }

    internal static readonly Queue<IPEndPoint> pluginsToRemove = new();

    internal static readonly Queue<PluginStruct> pluginsToAdd = new();

    internal static readonly Queue<Plugin> pluginsToRemoveFromDB = new();

    internal static readonly Queue<Plugin> pluginsToDelete = new();

    internal static readonly object PluginsListOperationLock = new();

    /// <summary>
    /// 持续检查并移除
    /// </summary>
    internal static void KeepCheckAndRemove()
    {
        var location = $"{nameof(PluginsNetwork)}.{nameof(KeepCheckAndRemove)}";

        System.Timers.Timer timer = new()
        {
            Interval = 10,
            AutoReset = true
        };

        timer.Elapsed += (_, _) =>
        {
            try
            {
                if (pluginsToAdd.Count > 0)
                {
                    List<PluginCard> pluginCardsToAdd = new();
                    int needAddCount = 0, addedCount = 0;
                    while (pluginsToAdd.Count > 0)
                    {
                        ++needAddCount;

                        PluginStruct pluginStruct = pluginsToAdd.Dequeue();

                        Dispatcher.UIThread.Post(() =>
                        {
                            PluginCard card = new(pluginStruct);
                            pluginCardsToAdd.Add(card);
                            lock ((object)addedCount)
                            {
                                ++addedCount;
                            }
                        });
                    }
                    while (needAddCount != addedCount) { }
                    foreach (var item in pluginCardsToAdd)
                    {
                        Program.PluginCards.Add(item);
                    }
                }

                if (pluginsToRemove.Count > 0)
                {
                    List<PluginCard> pluginCardsToRemove = new();
                    while (pluginsToRemove.Count > 0)
                    {
                        IPEndPoint endPoint = pluginsToRemove.Dequeue();
                        foreach (var item in Program.PluginCards)
                        {
                            if (item.pluginStruct.Tags["IPEndPoint"].Equals(endPoint.ToString()))
                            {
                                pluginCardsToRemove.Add(item);
                                break;
                            }
                        }
                    }
                    foreach (var item in pluginCardsToRemove)
                    {
                        Program.PluginCards.Remove(item);
                    }
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
        };

        timer.Start();
    }

    /// <summary>
    /// 断开了连接
    /// </summary>
    /// <param name="id">插件 id</param>
    internal static void Disconnect(IPEndPoint endPoint)
    {
        pluginsToRemove.Enqueue(endPoint);
    }

    /// <summary>
    /// 请求移除插件
    /// </summary>
    /// <param name="plugin">插件的安装信息</param>
    internal static void RequireRemovePlugin(Plugin plugin) => pluginsToRemoveFromDB.Enqueue(plugin);

    /// <summary>
    /// 请求删除插件
    /// </summary>
    /// <param name="plugin">插件的安装信息</param>
    internal static void RequireDeletePlugin(Plugin plugin) => pluginsToDelete.Enqueue(plugin);

    /// <summary>
    /// 持续检查移除和删除队列
    /// </summary>
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
