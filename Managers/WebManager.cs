using KitX_Dashboard.Servers;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace KitX_Dashboard.Managers;

public class WebManager : IDisposable
{
    public WebManager()
    {
        NetworkInterfaceRegistered = new();
    }

    internal PluginsServer? pluginsServer;
    internal DevicesServer? devicesServer;

    internal DevicesDiscoveryServer? devicesDiscoveryServer;

    internal ObservableCollection<string>? NetworkInterfaceRegistered;

    /// <summary>
    /// 开始执行网络相关服务
    /// </summary>
    /// <param name="startPluginsServer">是否启动插件服务器</param>
    /// <param name="startDevicesServer">是否启动设备服务器</param>
    /// <returns>网络管理器本身</returns>
    public WebManager Start(bool startPluginsServer = true, bool startDevicesServer = true)
    {
        new Thread(async () =>
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

                devicesDiscoveryServer = await new DevicesDiscoveryServer().Start();
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
    /// <param name="stopPluginsServer">是否停止插件服务器</param>
    /// <param name="stopDevicesServer">是否停止设备服务器</param>
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

        devicesDiscoveryServer?.Stop();

        return this;
    }

    /// <summary>
    /// 重启网络相关服务
    /// </summary>
    /// <param name="restartPluginsServer">是否重启插件服务器</param>
    /// <param name="restartDevicesServer">是否重启设备服务器</param>
    /// <param name="actionBeforeStarting">重新启动前要执行的操作</param>
    /// <returns>网络管理器本身</returns>
    public WebManager Restart(bool restartPluginsServer = true, bool restartDevicesServer = true,
        Action? actionBeforeStarting = null)
    {
        Stop(stopPluginsServer: restartPluginsServer, stopDevicesServer: restartDevicesServer);

        Task.Run(async () =>
        {
            await Task.Delay(Program.Config.Web.UDPSendFrequency + 100);

            actionBeforeStarting?.Invoke();

            Start(startPluginsServer: restartPluginsServer, startDevicesServer: restartDevicesServer);
        });
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


