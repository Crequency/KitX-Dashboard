﻿using Avalonia.Threading;
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
using JsonSerializer = System.Text.Json.JsonSerializer;

#pragma warning disable CS8605 // 取消装箱可能为 null 的值。
#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。

namespace KitX_Dashboard.Managers;

internal class PluginsManager
{
    /// <summary>
    /// 执行 Socket 消息
    /// </summary>
    /// <param name="msg">消息</param>
    internal static void Execute(string msg, IPEndPoint endPoint)
    {
        try
        {
            var pluginStruct = (PluginStruct)JsonSerializer.Deserialize(msg,
                typeof(PluginStruct));

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
            Log.Error(e, $"PluginsManager.Execute(msg) => msg: {msg}; {e.Message}");
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
                var location = $"{nameof(PluginsManager)}.{nameof(KeepCheckAndRemove)}()";
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
    /// 导入插件
    /// </summary>
    /// <param name="kxpfiles">.kxp files list</param>
    internal static void ImportPlugin(string[] kxpfiles, bool inGraphic = false)
    {
        string? workbasef = Environment.ProcessPath;
        string? workbase;
        if (workbasef == null)
            throw new Exception("Can not get path of \"KitX Dashboard.exe\"");
        else
        {
            workbase = Path.GetDirectoryName(workbasef);
            if (workbase == null)
                throw new Exception("Can not get path of \"KitX\"");
        }
        foreach (var item in kxpfiles)
        {
            try
            {
                KitX.KXP.Helper.Decoder decoder = new(item);
                Tuple<string, string> rst = decoder.GetLoaderAndPluginStruct();
                LoaderStruct loaderStruct = JsonSerializer.Deserialize<LoaderStruct>(rst.Item1);
                PluginStruct pluginStruct = JsonSerializer.Deserialize<PluginStruct>(rst.Item2);
                AppConfig? config = null;
                if (inGraphic) config = Program.Config;
                else config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(
                    Path.GetFullPath($"{GlobalInfo.ConfigPath}config.json")
                ));
                if (config == null)
                {
                    Console.WriteLine($"No config file found!");
                    if (!inGraphic) Environment.Exit(ErrorCodes.ConfigFileDidntExists);
                }
                string pluginsavedir = config?.App?.LocalPluginsFileFolder;
                if (pluginsavedir != null)
                    pluginsavedir = Path.GetFullPath(pluginsavedir);
                string thisplugindir = $"{pluginsavedir}/" +
                    $"{pluginStruct.PublisherName}_{pluginStruct.AuthorName}/" +
                    $"{pluginStruct.Name}/" +
                    $"{pluginStruct.Version}/";
                thisplugindir = Path.GetFullPath(thisplugindir);
                if (Directory.Exists(thisplugindir))
                    Directory.Delete(thisplugindir, true);
                _ = Directory.CreateDirectory(thisplugindir);
                _ = decoder.Decode(thisplugindir);

                if (!Program.PluginsList.Plugins.Exists(x => x.InstallPath.Equals(thisplugindir)))
                    Program.PluginsList.Plugins.Add(new()
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
        EventHandlers.Invoke(nameof(EventHandlers.PluginsListChanged));
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
        System.Timers.Timer timer = new()
        {
            Interval = 2000,
            AutoReset = true
        };
        timer.Elapsed += (_, _) =>
        {
            try
            {
                bool isPluginsListUpdated = false;

                if (pluginsToRemoveFromDB.Count > 0)
                {
                    isPluginsListUpdated = true;
                    while (pluginsToRemoveFromDB.Count > 0)
                    {
                        Plugin pg = pluginsToRemoveFromDB.Dequeue();
                        lock (PluginsListOperationLock)
                        {
                            Program.PluginsList.Plugins.RemoveAt(
                                Program.PluginsList.Plugins.FindIndex(
                                    x =>
                                    {
                                        if (x.InstallPath != null)
                                            return x.InstallPath.Equals(pg.InstallPath);
                                        else return false;
                                    }));
                        }
                    }
                }

                if (pluginsToDelete.Count > 0)
                {
                    isPluginsListUpdated = true;
                    while (pluginsToDelete.Count > 0)
                    {
                        Plugin pg = pluginsToDelete.Dequeue();
                        lock (PluginsListOperationLock)
                        {
                            Program.PluginsList.Plugins.RemoveAt(
                                Program.PluginsList.Plugins.FindIndex(
                                    x =>
                                    {
                                        if (x.InstallPath != null)
                                            return x.InstallPath.Equals(pg.InstallPath);
                                        else return false;
                                    }));
                        }
                        string pgfiledir = Path.GetFullPath(
                            $"{Program.Config.App.LocalPluginsFileFolder}/" +
                            $"{pg.PluginDetails.PublisherName}_{pg.PluginDetails.AuthorName}/" +
                            $"{pg.PluginDetails.Name}/{pg.PluginDetails.Version}/"
                        );
                        Directory.Delete(pgfiledir, true);
                    }
                }

                if (isPluginsListUpdated) EventHandlers.Invoke(nameof(EventHandlers.PluginsListChanged));
            }
            catch (Exception ex)
            {
                var location = $"{nameof(PluginsManager)}.{nameof(KeepCheckAndRemoveOrDelete)}()";
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        };
        timer.Start();
    }
}

#pragma warning restore CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning restore CS8605 // 取消装箱可能为 null 的值。

//                                   _----_
//                              ,__./#%  o `,
//                        .__.--%##%%     ,' \.
//                       /%%########%%    .====\
//                   __.;%##///         ,/
//             ____--     ,,,,,/      ,/
//            __====..,'''''''      ,/
//      ..__--      ,,,,,, ,,,,  ,./
//     /    ,,,,.'''`````'///////
//    /"""""              \/ \/
//                       &lt;&lt; &lt;&lt;
//                        \\ \\
//                         \\ \\
//                        --``-``===;
//                            \,  \,

