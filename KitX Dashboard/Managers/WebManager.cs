using KitX.Dashboard.Network.DevicesNetwork;
using KitX.Dashboard.Network.PluginsNetwork;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitX.Dashboard.Managers;

public class WebManager
{
    private static WebManager? _instance;

    public static WebManager Instance => _instance ??= new();

    internal readonly ObservableCollection<string> NetworkInterfaceRegistered = [];

    public async Task<WebManager> RunAsync(WebManagerOperationInfo info)
    {
        var location = $"{nameof(WebManager)}.{nameof(RunAsync)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            try
            {
                if (info.RunAll || info.RunPluginsServer)
                    PluginsServer.Instance.Run();

                if (info.RunAll || info.RunDevicesDiscoveryServer)
                    await DevicesDiscoveryServer.Instance.RunAsync();

                if (info.RunAll || info.RunDevicesServer)
                    await DevicesServer.Instance.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {JsonSerializer.Serialize(info)}");
            }
        }, location);

        return this;
    }

    public async Task<WebManager> CloseAsync(WebManagerOperationInfo info)
    {
        var location = $"{nameof(WebManager)}.{nameof(CloseAsync)}";

        try
        {
            if (info.CloseAll || info.CloseDevicesServer)
                await DevicesServer.Instance.CloseAsync();

            if (info.CloseAll || info.CloseDevicesDiscoveryServer)
            {
                await DevicesDiscoveryServer.Instance.CloseAsync().ContinueWith(
                    async server =>
                    {
                        await Task.Delay(ConfigManager.Instance.AppConfig.Web.UdpSendFrequency + 500);

                        server.Dispose();
                    }
                );

                while (DevicesDiscoveryServer.Instance.CloseDevicesDiscoveryServerRequest) { }
            }

            if (info.CloseAll || info.ClosePluginsServer)
                await PluginsServer.Instance.Close();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"In {location}: {ex.Message}");
        }

        return this;
    }

    public async Task<WebManager> RestartAsync(WebManagerOperationInfo info, Action? actionBeforeStarting = null)
    {
        await CloseAsync(info);

        actionBeforeStarting?.Invoke();

        await RunAsync(info);

        return this;
    }
}

public struct WebManagerOperationInfo
{
    public bool RunPluginsServer = true;

    public bool RunDevicesServer = true;

    public bool RunDevicesDiscoveryServer = true;

    public bool RunAll
    {
        readonly get => RunPluginsServer && RunDevicesServer && RunDevicesDiscoveryServer;
        set
        {
            RunPluginsServer = value;
            RunDevicesServer = value;
            RunDevicesDiscoveryServer = value;
        }
    }

    public bool ClosePluginsServer = true;

    public bool CloseDevicesServer = true;

    public bool CloseDevicesDiscoveryServer = true;

    public bool CloseAll
    {
        readonly get => ClosePluginsServer && CloseDevicesServer && CloseDevicesDiscoveryServer;
        set
        {
            ClosePluginsServer = value;
            CloseDevicesServer = value;
            CloseDevicesDiscoveryServer = value;
        }
    }

    public WebManagerOperationInfo()
    {

    }
}
