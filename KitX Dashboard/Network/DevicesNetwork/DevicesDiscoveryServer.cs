using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Names;
using KitX.Dashboard.Network.PluginsNetwork;
using KitX.Shared.Device;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitX.Dashboard.Network.DevicesNetwork;

public class DevicesDiscoveryServer
{
    private static DevicesDiscoveryServer? _instance;

    public static DevicesDiscoveryServer Instance => _instance ??= new DevicesDiscoveryServer();

    private static UdpClient? UdpSender = null;

    private static UdpClient? UdpReceiver = null;

    private static System.Timers.Timer? UdpSendTimer = null;

    private static int DeviceInfoUpdatedTimes = 0;

    private static int LastTimeToOSVersionUpdated = 0;

    private static readonly List<int> SupportedNetworkInterfacesIndexes = [];

    private static bool disposed = false;

    private static Action<byte[], int?, string>? onReceive = null;

    internal static bool CloseDevicesDiscoveryServerRequest = false;

    internal static readonly Queue<string> Messages2BroadCast = new();

    internal static DeviceInfo DefaultDeviceInfo = NetworkHelper.GetDeviceInfo();

    private static ServerStatus status = ServerStatus.Pending;

    internal static ServerStatus Status
    {
        get => status;
        set
        {
            status = value;
        }
    }

    private static void FindSupportNetworkInterfaces(List<UdpClient> clients, IPAddress multicastAddress)
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

    private static void UpdateDefaultDeviceInfo()
    {
        DefaultDeviceInfo.IsMainDevice = ConstantTable.IsMainMachine;
        DefaultDeviceInfo.SendTime = DateTime.UtcNow;
        DefaultDeviceInfo.Device
            .ResetIPv4(NetworkHelper.GetInterNetworkIPv4())
            .ResetIPv6(NetworkHelper.GetInterNetworkIPv6())
            ;
        DefaultDeviceInfo.PluginsServerPort = ConstantTable.PluginsServerPort;
        DefaultDeviceInfo.PluginsCount = PluginsServer.Instance.PluginConnectors.Count;
        DefaultDeviceInfo.IsMainDevice = ConstantTable.IsMainMachine;
        DefaultDeviceInfo.DevicesServerPort = ConstantTable.DevicesServerPort;
        DefaultDeviceInfo.DevicesServerBuildTime = ConstantTable.ServerBuildTime;

        if (LastTimeToOSVersionUpdated > Instances.ConfigManager.AppConfig.IO.OperatingSystemVersionUpdateInterval)
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

        IPEndPoint multicast = new(
            IPAddress.Parse(Instances.ConfigManager.AppConfig.Web.UdpBroadcastAddress),
            Instances.ConfigManager.AppConfig.Web.UdpPortReceive
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
            Interval = Instances.ConfigManager.AppConfig.Web.UdpSendFrequency,
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

        IPEndPoint multicast = new(IPAddress.Any, 0);
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

                    onReceive?.Invoke(bytes, null, client);

                    var result = bytes.ToUTF8();

                    Log.Information($"UDP From: {client,-21}, Receive: {result}");

                    try
                    {
                        DevicesManager.Update(
                            JsonSerializer.Deserialize<DeviceInfo>(result)
                        );
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"When trying to deserialize `{result}`");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, $"In {location}: {e.Message}");

                Status = ServerStatus.Errored;
            }

            await CloseAsync();

        }).Start();
    }

    private static void Init()
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

        Init();

        UdpSender = new(
            Instances.ConfigManager.AppConfig.Web.UdpPortSend,
            AddressFamily.InterNetwork
        )
        {
            EnableBroadcast = true,
            MulticastLoopback = true
        };

        UdpReceiver = new(
            new IPEndPoint(
                IPAddress.Any,
                Instances.ConfigManager.AppConfig.Web.UdpPortReceive
            )
        );

        await TasksManager.RunTaskAsync(() =>
        {
            try
            {
                FindSupportNetworkInterfaces(
                    [UdpSender, UdpReceiver],
                    IPAddress.Parse(Instances.ConfigManager.AppConfig.Web.UdpBroadcastAddress)
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
        ); // 开始发送组播报文

        await TasksManager.RunTaskAsync(
            MultiDevicesBroadCastReceive,
            nameof(MultiDevicesBroadCastReceive)
        ); // 开始接收组播报文

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

            Status = ServerStatus.Pending;
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

    public async Task<DevicesDiscoveryServer> Send(byte[] content, string target)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesDiscoveryServer> Broadcast(byte[] content)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesDiscoveryServer> BroadCast(byte[] content, Func<TcpClient, bool>? pattern)
    {
        await Task.Run(() => { });

        return this;
    }

    public DevicesDiscoveryServer OnReceive(Action<byte[], int?, string> action)
    {
        onReceive = action;

        return this;
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
