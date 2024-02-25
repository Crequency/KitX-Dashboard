using KitX.Dashboard.Models.Network;
using KitX.Dashboard.Network.DevicesNetwork;
using KitX.Dashboard.Network.PluginsNetwork;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitX.Dashboard.Managers;

public class WebManager : IDisposable
{
    internal ObservableCollection<string>? NetworkInterfaceRegistered;

    public WebManager()
    {
        NetworkInterfaceRegistered = [];
    }

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
                    DevicesManager.Start();
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
                DevicesManager.Stop();

            if (info.CloseAll || info.CloseDevicesDiscoveryServer)
            {
                await DevicesDiscoveryServer.Instance.CloseAsync().ContinueWith(
                    async server =>
                    {
                        await Task.Delay(Instances.ConfigManager.AppConfig.Web.UdpSendFrequency + 500);

                        server.Dispose();
                    }
                );

                while (DevicesDiscoveryServer.CloseDevicesDiscoveryServerRequest) { }
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
