using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Names;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Device;
using Serilog;

namespace KitX.Dashboard.Network.DevicesNetwork;

public class DevicesDiscoveryServer
{
    private static DevicesDiscoveryServer? _instance;

    public static DevicesDiscoveryServer Instance => _instance ??= new();

    private UdpClient? UdpSender = null;

    private UdpClient? UdpReceiver = null;

    private System.Timers.Timer? UdpSendTimer = null;

    private int DeviceInfoUpdatedTimes = 0;

    private int LastTimeToOSVersionUpdated = 0;

    private readonly List<int> SupportedNetworkInterfacesIndexes = [];

    private bool disposed = false;

    internal bool CloseDevicesDiscoveryServerRequest = false;

    internal readonly Queue<string> Messages2BroadCast = new();

    internal DeviceInfo DefaultDeviceInfo = NetworkHelper.GetDeviceInfo();

    private ServerStatus status = ServerStatus.Pending;

    internal ServerStatus Status
    {
        get => status;
        set
        {
            status = value;
        }
    }

    public DevicesDiscoveryServer()
    {
        DevicesOrganizer.Run();

        Initialize();
    }

    private void Initialize()
    {
        disposed = false;

        DeviceInfoUpdatedTimes = 0;

        LastTimeToOSVersionUpdated = 0;

        CloseDevicesDiscoveryServerRequest = false;

        SupportedNetworkInterfacesIndexes.Clear();

        Messages2BroadCast.Clear();

        DefaultDeviceInfo = NetworkHelper.GetDeviceInfo();
    }

    public async Task<DevicesDiscoveryServer> RunAsync()
    {
        if (Status != ServerStatus.Pending) return this;

        Status = ServerStatus.Starting;

        Initialize();

        UdpSender = new(
            ConfigManager.Instance.AppConfig.Web.UdpPortSend,
            AddressFamily.InterNetwork
        )
        {
            EnableBroadcast = true,
            MulticastLoopback = true
        };

        UdpReceiver = new(
            new IPEndPoint(
                IPAddress.Any,
                ConfigManager.Instance.AppConfig.Web.UdpPortReceive
            )
        );

        await TasksManager.RunTaskAsync(() =>
        {
            try
            {
                FindSupportNetworkInterfaces(
                    [UdpSender, UdpReceiver],
                    IPAddress.Parse(ConfigManager.Instance.AppConfig.Web.UdpBroadcastAddress)
                ); // 寻找所有支持的网络适配器
            }
            catch (Exception ex)
            {
                var location = $"{nameof(DevicesServer)}.{nameof(RunAsync)}";
                Log.Warning(ex, $"In {location}: {ex.Message}");
            }
        }, nameof(FindSupportNetworkInterfaces));

        await TasksManager.RunTaskAsync(
            MultiDevicesBroadCastSend,
            nameof(MultiDevicesBroadCastSend)
        );

        await TasksManager.RunTaskAsync(
            MultiDevicesBroadCastReceive,
            nameof(MultiDevicesBroadCastReceive)
        );

        Status = ServerStatus.Running;

        return this;
    }

    public async Task<DevicesDiscoveryServer> CloseAsync()
    {
        if (Status != ServerStatus.Running) return this;

        await Task.Run(() =>
        {
            Status = ServerStatus.Stopping;

            CloseDevicesDiscoveryServerRequest = true;
        });

        return this;
    }

    public async Task<DevicesDiscoveryServer> Restart()
    {
        await Task.Run(async () =>
        {
            await CloseAsync();

            await RunAsync();
        });

        return this;
    }

    private void FindSupportNetworkInterfaces(List<UdpClient> clients, IPAddress multicastAddress)
    {
        var multicastGroupJoinedInterfacesCount = 0;

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var adapterProperties = adapter.GetIPProperties();

            if (adapterProperties is null) continue;

            if (!NetworkHelper.CheckNetworkInterface(adapter, adapterProperties)) continue;

            var unicastIPAddresses = adapterProperties.UnicastAddresses;

            if (unicastIPAddresses is null) continue;

            var p = adapterProperties.GetIPv4Properties();

            if (p is null) continue;    // IPv4 is not configured on this adapter

            SupportedNetworkInterfacesIndexes.Add(IPAddress.HostToNetworkOrder(p.Index));

            foreach (var ipAddress in unicastIPAddresses
                .Select(x => x.Address)
                .Where(x => x.AddressFamily == AddressFamily.InterNetwork))
            {
                try
                {
                    foreach (var udpClient in clients)
                        udpClient?.JoinMulticastGroup(multicastAddress, ipAddress);

                    Instances.WebManager?.NetworkInterfaceRegistered?.Add(adapter.Name);

                    ++multicastGroupJoinedInterfacesCount;
                }
                catch (Exception ex)
                {
                    var location = $"{nameof(DevicesServer)}.{nameof(FindSupportNetworkInterfaces)}";

                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }
        }

        Instances.SignalTasksManager?.RaiseSignal(nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal));

        Log.Information($"Find {SupportedNetworkInterfacesIndexes.Count} supported network interfaces.");

        Log.Information($"Joined {multicastGroupJoinedInterfacesCount} multicast groups.");
    }

    private void UpdateDefaultDeviceInfo()
    {
        DefaultDeviceInfo.IsMainDevice = ConstantTable.IsMainMachine;
        DefaultDeviceInfo.SendTime = DateTime.UtcNow;
        DefaultDeviceInfo.Device
            .ResetIPv4(NetworkHelper.GetInterNetworkIPv4())
            .ResetIPv6(NetworkHelper.GetInterNetworkIPv6())
            ;
        DefaultDeviceInfo.PluginsServerPort = ConstantTable.PluginsServerPort;
        DefaultDeviceInfo.PluginsCount = ViewInstances.PluginInfos.Count;
        DefaultDeviceInfo.IsMainDevice = ConstantTable.IsMainMachine;
        DefaultDeviceInfo.DevicesServerPort = ConstantTable.DevicesServerPort;
        DefaultDeviceInfo.DevicesServerBuildTime = ConstantTable.ServerBuildTime;

        if (LastTimeToOSVersionUpdated > ConfigManager.Instance.AppConfig.IO.OperatingSystemVersionUpdateInterval)
        {
            LastTimeToOSVersionUpdated = 0;
            DefaultDeviceInfo.DeviceOSVersion = NetworkHelper.TryGetOSVersionString() ?? "";
        }

        ++DeviceInfoUpdatedTimes;
        ++LastTimeToOSVersionUpdated;

        if (DeviceInfoUpdatedTimes < 0) DeviceInfoUpdatedTimes = 0;
    }

    private void MultiDevicesBroadCastSend()
    {
        var location = $"{nameof(DevicesDiscoveryServer)}.{nameof(MultiDevicesBroadCastSend)}";

        var multicast = new IPEndPoint(
            IPAddress.Parse(ConfigManager.Instance.AppConfig.Web.UdpBroadcastAddress),
            ConfigManager.Instance.AppConfig.Web.UdpPortReceive
        );

        UdpSender?.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true
        );

        var erroredInterfacesIndexes = new List<int>();
        var erroredInterfacesIndexesTTL = 60;

        UdpSendTimer = new()
        {
            Interval = ConfigManager.Instance.AppConfig.Web.UdpSendFrequency,
            AutoReset = true
        };

        UdpSendTimer.Elapsed += (_, _) =>
        {
            var closingRequest = CloseDevicesDiscoveryServerRequest;

            --erroredInterfacesIndexesTTL;

            if (erroredInterfacesIndexesTTL <= 0)
            {
                erroredInterfacesIndexesTTL = 60;
                erroredInterfacesIndexes.Clear();
            }

            UpdateDefaultDeviceInfo();

            if (closingRequest)
                DefaultDeviceInfo.SendTime -= TimeSpan.FromSeconds(20);

            var sendText = JsonSerializer.Serialize(DefaultDeviceInfo);
            var sendBytes = sendText.FromUTF8();

            foreach (var item in SupportedNetworkInterfacesIndexes)
            {
                if (!ConstantTable.Running) break;

                //  如果错误网络适配器中存在当前项的记录, 跳过
                if (erroredInterfacesIndexes.Contains(item)) continue;

                try
                {
                    UdpSender?.Client.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.MulticastInterface,
                        item
                    );
                    UdpSender?.Send(sendBytes, sendBytes.Length, multicast);

                    //  将自定义广播消息全部发送
                    while (Messages2BroadCast.Count > 0)
                    {
                        var messageBytes = Messages2BroadCast.Dequeue().FromUTF8();
                        UdpSender?.Send(messageBytes, messageBytes.Length, multicast);
                    }
                }
                catch (Exception ex)
                {
                    // 该网络适配器存在异常, 暂时记录到错误网络适配器中
                    if (!erroredInterfacesIndexes.Contains(item))
                        erroredInterfacesIndexes.Add(item);

                    Log.Warning(
                        ex,
                        $"In {location}: Errored interface index: {item}, recorded."
                    );
                }
            }

            if (closingRequest)
            {
                UdpSendTimer?.Stop();
                UdpSendTimer?.Close();

                UdpSender?.Close();

                UdpReceiver?.Close();

                CloseDevicesDiscoveryServerRequest = false;
            }
        };

        UdpSendTimer.Start();
    }

    private void MultiDevicesBroadCastReceive()
    {
        var location = $"{nameof(DevicesDiscoveryServer)}.{nameof(MultiDevicesBroadCastReceive)}";

        var multicast = new IPEndPoint(IPAddress.Any, 0);

        UdpReceiver?.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true
        );

        new Thread(async () =>
        {
            try
            {
                while (ConstantTable.Running && !CloseDevicesDiscoveryServerRequest)
                {
                    var bytes = UdpReceiver?.Receive(ref multicast);
                    var client = $"{multicast.Address}:{multicast.Port}";

                    if (bytes is null) continue;    //  null byte[] cause exception in next line.

                    var result = bytes.ToUTF8();

                    Log.Information($"UDP From: {client,-21}, Receive: {result}");

                    try
                    {
                        var info = JsonSerializer.Deserialize<DeviceInfo>(result);

                        if (info is not null)
                            EventService.Invoke(
                                nameof(EventService.OnReceivingDeviceInfo),
                                [info]
                            );
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"When trying to deserialize `{result}`");
                    }
                }

                Status = ServerStatus.Pending;
            }
            catch (Exception e)
            {
                Log.Error(e, $"In {location}: {e.Message}");

                Status = ServerStatus.Errored;
            }

            await CloseAsync();

        }).Start();
    }

    public void Dispose()
    {
        if (disposed) return;

        disposed = true;

        CloseDevicesDiscoveryServerRequest = false;

        UdpSender?.Dispose();
        UdpReceiver?.Dispose();

        GC.Collect();
    }
}

public static class DevicesDiscoveryServerExtensions
{
    public static bool IsOffline(this DeviceInfo info)
        => DateTime.UtcNow - info.SendTime.ToUniversalTime() > new TimeSpan(
            0, 0, ConfigManager.Instance.AppConfig.Web.DeviceInfoTTLSeconds
        );

    public static bool IsCurrentDevice(this DeviceInfo info)
        => info.IsSameDevice(DevicesDiscoveryServer.Instance.DefaultDeviceInfo);

    public static bool IsSameDevice(this DeviceInfo info, DeviceInfo target) => info.Device.IsSameDevice(target.Device);

    public static void UpdateTo(this DeviceInfo info, DeviceInfo target)
    {
        var type = typeof(DeviceInfo);

        var fields = type.GetFields();

        foreach (var field in fields)
        {
            var firstValue = field.GetValue(info);
            var secondValue = field.GetValue(target);

            if (firstValue?.Equals(secondValue) ?? false)
                continue;

            field.SetValue(info, secondValue);
        }

        var properties = type.GetProperties();

        foreach (var property in properties)
        {
            object? firstValue = property.GetValue(info);
            object? secondValue = property.GetValue(target);

            if (firstValue?.Equals(secondValue) ?? false)
                continue;

            property.SetValue(info, secondValue);
        }
    }
}
