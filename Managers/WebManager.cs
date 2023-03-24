﻿using KitX_Dashboard.Servers;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Threading;

namespace KitX_Dashboard.Managers;

public class WebManager : IDisposable
{
    public WebManager()
    {
        NetworkInterfaceRegistered = new();
    }

    internal PluginsServer? pluginsServer;
    internal DevicesServer? devicesServer;

    internal ObservableCollection<string>? NetworkInterfaceRegistered;

    /// <summary>
    /// 开始执行网络相关服务
    /// </summary>
    /// <returns>网络管理器本身</returns>
    public WebManager Start(bool startPluginsServer = true, bool startDevicesServer = true)
    {
        new Thread(() =>
        {
            try
            {
                Log.Information("WebManager: Starting...");

                if (startDevicesServer)
                {
                    DevicesManager.InitEvents();
                    DevicesManager.KeepCheckAndRemove();
                    DevicesManager.Watch4MainDevice();

                    devicesServer = new();
                    devicesServer.Start();
                }

                if (startPluginsServer)
                {
                    PluginsManager.KeepCheckAndRemove();
                    PluginsManager.KeepCheckAndRemoveOrDelete();

                    pluginsServer = new();
                    pluginsServer.Start();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "In WebManager Start");
            }
        }).Start();

        return this;
    }

    /// <summary>
    /// 停止执行网络相关服务
    /// </summary>
    /// <returns>网络管理器本身</returns>
    public WebManager Stop(bool stopPluginsServer = true, bool stopDevicesServer = true)
    {
        if (stopPluginsServer)
        {
            pluginsServer?.Stop();
            pluginsServer?.Dispose();
        }

        if (stopDevicesServer)
        {
            devicesServer?.Stop();
            devicesServer?.Dispose();
        }

        return this;
    }

    /// <summary>
    /// 重启网络相关服务
    /// </summary>
    /// <returns>网络管理器本身</returns>
    public WebManager Restart()
    {
        Stop();
        Start();
        return this;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        pluginsServer?.Dispose();
        devicesServer?.Dispose();
        GC.SuppressFinalize(this);
    }
}

//                                         .....
//                                    .e$$$$$$$$$$$$$$e.
//                                  z$$ ^$$$$$$$$$$$$$$$$$.
//                                .$$$* J$$$$$$$$$$$$$$$$$$$e
//                               .$"  .$$$$$$$$$$$$$$$$$$$$$$*-
//                              .$  $$$$$$$$$$$$$$$$***$$  .ee"
//                 z**$$        $$r ^**$$$$$$$$$*" .e$$$$$$*"
//                " -\e$$      4$$$$.         .ze$$$""""
//               4 z$$$$$      $$$$$$$$$$$$$$$$$$$$"
//               $$$$$$$$     .$$$$$$$$$$$**$$$$*"
//             z$$"    $$     $$$$P*""     J$*$$c
//            $$"      $$F   .$$$          $$ ^$$
//           $$        *$$c.z$$$          $$   $$
//          $P          $$$$$$$          4$F   4$
//         dP            *$$$"           $$    '$r
//        .$                            J$"     $"
//        $                             $P     4$
//        F                            $$      4$
//                                    4$%      4$
//                                    $$       4$
//                                   d$"       $$
//                                   $P        $$
//                                  $$         $$
//                                 4$%         $$
//                                 $$          $$
//                                d$           $$
//                                $F           "3
//                         r=4e="  ...  ..rf   .  ""%
//                        $**$*"^""=..^4*=4=^""  ^"""


