using KitX_Dashboard.Network;
using Serilog;
using System;
using System.Collections.ObjectModel;
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
    /// <param name="startAll">是否启动全部</param>
    /// <param name="startPluginsServer">是否启动插件服务器</param>
    /// <param name="startDevicesServer">是否启动设备服务器</param>
    /// <param name="startDevicesDiscoveryServer">是否启动设备自发现服务器</param>
    /// <returns>网络管理器本身</returns>
    public async Task<WebManager> Start
    (
        bool startAll = true,

        bool startPluginsServer = false,
        bool startDevicesServer = false,
        bool startDevicesDiscoveryServer = false
    )
    {
        var location = $"{nameof(WebManager)}.{nameof(Start)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            try
            {
                if (startAll || startDevicesDiscoveryServer)
                    devicesDiscoveryServer = await new DevicesDiscoveryServer().Start();

                if (startAll || startDevicesServer)
                {
                    DevicesManager.InitEvents();
                    DevicesManager.KeepCheckAndRemove();
                    DevicesManager.Watch4MainDevice();

                    devicesServer = await new DevicesServer().Start();
                }

                if (startAll || startPluginsServer)
                {
                    PluginsManager.KeepCheckAndRemove();
                    PluginsManager.KeepCheckAndRemoveOrDelete();

                    pluginsServer = await new PluginsServer().Start();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: " +
                    $"{nameof(startPluginsServer)}: {startPluginsServer}," +
                    $"{nameof(startDevicesServer)}: {startDevicesServer}," +
                    $"{nameof(startDevicesDiscoveryServer)}: {startDevicesDiscoveryServer}");
            }
        }, "WebManager.Start");

        return this;
    }

    /// <summary>
    /// 停止执行网络相关服务
    /// </summary>
    /// <param name="stopPluginsServer">是否停止插件服务器</param>
    /// <param name="stopDevicesServer">是否停止设备服务器</param>
    /// <returns>网络管理器本身</returns>
    public WebManager Stop
    (
        bool stopAll = true,

        bool stopPluginsServer = true,
        bool stopDevicesServer = true,
        bool stopDevicesDiscoveryServer = true
    )
    {
        if (stopAll || stopPluginsServer)
            pluginsServer?.Stop().ContinueWith(
                server => server.Dispose()
            );

        if (stopAll || stopDevicesServer)
            devicesServer?.Stop().ContinueWith(
                server => server.Dispose()
            );

        if (stopAll || stopDevicesDiscoveryServer)
            devicesDiscoveryServer?.Stop().ContinueWith(
                server => server.Dispose()
            );

        return this;
    }

    /// <summary>
    /// 重启网络相关服务
    /// </summary>
    /// <param name="restartPluginsServer">是否重启插件服务器</param>
    /// <param name="restartDevicesServer">是否重启设备服务器</param>
    /// <param name="actionBeforeStarting">重新启动前要执行的操作</param>
    /// <returns>网络管理器本身</returns>
    public WebManager Restart
    (
        bool restartAll = true,

        bool restartPluginsServer = false,
        bool restartDevicesServer = false,
        bool restartDevicesDiscoveryServer = false,

        Action? actionBeforeStarting = null
    )
    {
        Stop(
            restartAll,
            restartPluginsServer,
            restartDevicesServer,
            restartDevicesDiscoveryServer
        );

        Task.Run(async () =>
        {
            await Task.Delay(Program.Config.Web.UDPSendFrequency + 100);

            actionBeforeStarting?.Invoke();

            await Start(
                restartAll,
                restartPluginsServer,
                restartDevicesServer,
                restartDevicesDiscoveryServer
            );
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
        devicesDiscoveryServer?.Dispose();

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


