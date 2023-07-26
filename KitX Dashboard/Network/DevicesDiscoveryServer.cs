using Common.BasicHelper.Utils.Extensions;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Interfaces.Network;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Names;
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

namespace KitX_Dashboard.Network;

/// <summary>
/// 设备自发现网络服务器
/// </summary>
internal class DevicesDiscoveryServer : IKitXServer<DevicesDiscoveryServer>
{
    private static UdpClient? UdpSender = null;

    private static UdpClient? UdpReceiver = null;

    private static System.Timers.Timer? UdpSendTimer = null;

    private static int DeviceInfoStructUpdatedTimes = 0;

    private static int LastTimeToOSVersionUpdated = 0;

    private static readonly List<int> SupportedNetworkInterfacesIndexes = new();

    private static bool disposed = false;

    private static Action<byte[], int?, string>? onReceive = null;

    internal static bool CloseDevicesDiscoveryServerRequest = false;

    internal static readonly Queue<string> Messages2BroadCast = new();

    internal static DeviceInfoStruct DefaultDeviceInfoStruct = NetworkHelper.GetDeviceInfo();

    private static ServerStatus status = ServerStatus.Pending;

    internal static ServerStatus Status
    {
        get => status;
        set
        {
            status = value;
        }
    }

    /// <summary>
    /// 寻找受支持的网络适配器并把 UDP 客户端加入组播
    /// </summary>
    private static void FindSupportNetworkInterfaces(List<UdpClient> clients, IPAddress multicastAddress)
    {
        var multicastGroupJoinedInterfacesCount = 0;

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var adapterProperties = adapter.GetIPProperties();
            if (adapterProperties is null) continue;

            try
            {
                var logs = adapter.Dump2Lines();
                for (int i = 0; i < logs.Length; i++)
                    Log.Information(logs[i]);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Logging network interface items.");
            }

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

                    Program.WebManager?.NetworkInterfaceRegistered?.Add(adapter.Name);

                    ++multicastGroupJoinedInterfacesCount;
                }
                catch (Exception ex)
                {
                    var location = $"{nameof(DevicesServer)}.{nameof(FindSupportNetworkInterfaces)}";
                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }
        }

        Program.SignalTasksManager?.RaiseSignal(nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal));

        Log.Information($"" +
            $"Find {SupportedNetworkInterfacesIndexes.Count} supported network interfaces.");
        Log.Information($"" +
            $"Joined {multicastGroupJoinedInterfacesCount} multicast groups.");
    }

    /// <summary>
    /// 更新默认设备信息结构
    /// </summary>
    private static void UpdateDefaultDeviceInfoStruct()
    {
        DefaultDeviceInfoStruct.IsMainDevice = GlobalInfo.IsMainMachine;
        DefaultDeviceInfoStruct.SendTime = DateTime.UtcNow;
        DefaultDeviceInfoStruct.IPv4 = NetworkHelper.GetInterNetworkIPv4();
        DefaultDeviceInfoStruct.IPv6 = NetworkHelper.GetInterNetworkIPv6();
        DefaultDeviceInfoStruct.PluginServerPort = GlobalInfo.PluginServerPort;
        DefaultDeviceInfoStruct.PluginsCount = Program.PluginCards.Count;
        DefaultDeviceInfoStruct.IsMainDevice = GlobalInfo.IsMainMachine;
        DefaultDeviceInfoStruct.DeviceServerPort = GlobalInfo.DeviceServerPort;
        DefaultDeviceInfoStruct.DeviceServerBuildTime = GlobalInfo.ServerBuildTime;

        if (LastTimeToOSVersionUpdated > ConfigManager.AppConfig.IO.OperatingSystemVersionUpdateInterval)
        {
            LastTimeToOSVersionUpdated = 0;
            DefaultDeviceInfoStruct.DeviceOSVersion = NetworkHelper.TryGetOSVersionString();
        }

        ++DeviceInfoStructUpdatedTimes;
        ++LastTimeToOSVersionUpdated;

        if (DeviceInfoStructUpdatedTimes < 0) DeviceInfoStructUpdatedTimes = 0;
    }

    /// <summary>
    /// 多设备广播发送方法
    /// </summary>
    private void MultiDevicesBroadCastSend()
    {
        var location = $"{nameof(DevicesDiscoveryServer)}.{nameof(MultiDevicesBroadCastSend)}";

        IPEndPoint multicast = new(
            IPAddress.Parse(ConfigManager.AppConfig.Web.UDPBroadcastAddress),
            ConfigManager.AppConfig.Web.UDPPortReceive
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
            Interval = ConfigManager.AppConfig.Web.UDPSendFrequency,
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

            UpdateDefaultDeviceInfoStruct();

            if (closingRequest)
                DefaultDeviceInfoStruct.SendTime -= TimeSpan.FromSeconds(20);

            var sendText = JsonSerializer.Serialize(DefaultDeviceInfoStruct);
            var sendBytes = sendText.FromUTF8();

            foreach (var item in SupportedNetworkInterfacesIndexes)
            {
                if (!GlobalInfo.Running) break;

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

    /// <summary>
    /// 多设备广播接收方法
    /// </summary>
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
                while (GlobalInfo.Running && !CloseDevicesDiscoveryServerRequest)
                {
                    var bytes = UdpReceiver?.Receive(ref multicast);
                    var client = $"{multicast.Address}:{multicast.Port}";

                    if (bytes is null) continue;    //  null byte[] cause exception in next line.

                    onReceive?.Invoke(bytes, null, client);

                    var result = bytes.ToUTF8();

                    Log.Information($"UDP From: {client,-21}, Receive: {result}");

                    try
                    {
                        DevicesNetwork.Update(
                            JsonSerializer.Deserialize<DeviceInfoStruct>(result)
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

            await Stop();

        }).Start();
    }

    private static void Init()
    {
        disposed = false;

        DeviceInfoStructUpdatedTimes = 0;

        LastTimeToOSVersionUpdated = 0;

        CloseDevicesDiscoveryServerRequest = false;

        SupportedNetworkInterfacesIndexes.Clear();

        Messages2BroadCast.Clear();

        DefaultDeviceInfoStruct = NetworkHelper.GetDeviceInfo();
    }

    public async Task<DevicesDiscoveryServer> Start()
    {
        if (Status != ServerStatus.Pending) return this;

        Status = ServerStatus.Starting;

        Init();

        UdpSender = new(
            ConfigManager.AppConfig.Web.UDPPortSend,
            AddressFamily.InterNetwork
        )
        {
            EnableBroadcast = true,
            MulticastLoopback = true
        };

        UdpReceiver = new(
            new IPEndPoint(
                IPAddress.Any,
                ConfigManager.AppConfig.Web.UDPPortReceive
            )
        );

        await TasksManager.RunTaskAsync(() =>
        {
            try
            {
                FindSupportNetworkInterfaces(
                    new()
                    {
                        UdpSender, UdpReceiver
                    },
                    IPAddress.Parse(ConfigManager.AppConfig.Web.UDPBroadcastAddress)
                ); // 寻找所有支持的网络适配器
            }
            catch (Exception ex)
            {
                var location = $"{nameof(DevicesServer)}.{nameof(Start)}";
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

    public async Task<DevicesDiscoveryServer> Stop()
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
            await Stop();

            await Start();
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

    /// <summary>
    /// 设定当接收到数据时的处理代码
    /// </summary>
    /// <param name="action">处理代码, 参数一为接收到的数据 (byte[]), 参数二是数据发送者, ip:port</param>
    /// <returns>设备自发现网络服务器本身</returns>
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

        GC.SuppressFinalize(this);
    }
}
