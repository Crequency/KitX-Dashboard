using KitX.Dashboard.Network;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace KitX.Dashboard.Managers;

public class WebManager : IDisposable
{
    internal PluginsServer? pluginsServer;
    internal DevicesDiscoveryServer? devicesDiscoveryServer;

    internal ObservableCollection<string>? NetworkInterfaceRegistered;

    public WebManager()
    {
        NetworkInterfaceRegistered = new();
    }









    public async Task<WebManager> Start
    (
        bool startAll = true,

        bool startPluginsNetwork = false,
        bool startDevicesNetwork = false,
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

                if (startAll || startDevicesNetwork)
                    DevicesNetwork.Start();

                if (startAll || startPluginsNetwork)
                {
                    PluginsNetwork.KeepCheckAndRemove();
                    PluginsNetwork.KeepCheckAndRemoveOrDelete();

                    pluginsServer = await new PluginsServer().Start();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: " +
                    $"{nameof(startPluginsNetwork)}: {startPluginsNetwork}," +
                    $"{nameof(startDevicesNetwork)}: {startDevicesNetwork}," +
                    $"{nameof(startDevicesDiscoveryServer)}: {startDevicesDiscoveryServer}");
            }
        }, location);

        return this;
    }







    public WebManager Stop
    (
        bool stopAll = true,

        bool stopPluginsServices = false,
        bool stopDevicesServices = false,
        bool stopDevicesDiscoveryServer = false
    )
    {
        var location = $"{nameof(WebManager)}.{nameof(Stop)}";

        try
        {
            if (stopAll || stopPluginsServices)
                pluginsServer?.Stop().ContinueWith(
                    server => server.Dispose()
                );

            if (stopAll || stopDevicesServices)
                DevicesNetwork.Stop();

            if (stopAll || stopDevicesDiscoveryServer)
            {
                devicesDiscoveryServer?.Stop().ContinueWith(
                    async server =>
                    {
                        await Task.Delay(ConfigManager.AppConfig.Web.UDPSendFrequency + 500);

                        server.Dispose();
                    }
                );

                while (DevicesDiscoveryServer.CloseDevicesDiscoveryServerRequest) { }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"In {location}: {ex.Message}");
        }

        return this;
    }








    public WebManager Restart
    (
        bool restartAll = true,

        bool restartPluginsServices = false,
        bool restartDevicesServices = false,
        bool restartDevicesDiscoveryServer = false,

        Action? actionBeforeStarting = null
    )
    {
        Stop(
            restartAll,
            restartPluginsServices,
            restartDevicesServices,
            restartDevicesDiscoveryServer
        );

        Task.Run(async () =>
        {
            await Task.Delay(ConfigManager.AppConfig.Web.UDPSendFrequency + 100);

            actionBeforeStarting?.Invoke();

            await Start(
                restartAll,
                restartPluginsServices,
                restartDevicesServices,
                restartDevicesDiscoveryServer
            );
        });

        return this;
    }




    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
