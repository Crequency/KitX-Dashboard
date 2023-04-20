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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitX_Dashboard.Servers;

internal class DevicesDiscoveryServer : IKitXServer<DevicesDiscoveryServer>
{
    private static UdpClient? UdpSender = null;

    private static UdpClient? UdpReceiver = null;

    private static System.Timers.Timer? UdpSendTimer = null;

    private static int DeviceInfoStructUpdatedTimes = 0;

    private static int LastTimeToOSVersionUpdated = 0;

    private static bool CloseDevicesDiscoveryServerRequest = false;

    private static readonly List<int> SupportedNetworkInterfacesIndexes = new();

    private static ServerStatus status = ServerStatus.Unknown;

    internal static readonly Queue<string> Messages2BroadCast = new();

    internal static DeviceInfoStruct DefaultDeviceInfoStruct = NetworkHelper.GetDeviceInfo();

    internal static ServerStatus Status
    {
        get => status;
        set
        {
            status = value;
        }
    }

    /// <summary>
    /// 寻找受支持的网络适配器并把UDP客户端加入组播
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

        Program.TasksManager?.RaiseSignal(nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal));

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

        if (LastTimeToOSVersionUpdated > Program.Config.IO.OperatingSystemVersionUpdateInterval)
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
            IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress),
            Program.Config.Web.UDPPortReceive
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
            Interval = Program.Config.Web.UDPSendFrequency,
            AutoReset = true
        };
        UdpSendTimer.Elapsed += async (_, _) =>
        {
            --erroredInterfacesIndexesTTL;
            if (erroredInterfacesIndexesTTL <= 0)
            {
                erroredInterfacesIndexesTTL = 60;
                erroredInterfacesIndexes.Clear();
            }

            UpdateDefaultDeviceInfoStruct();

            if (CloseDevicesDiscoveryServerRequest)
                DefaultDeviceInfoStruct.SendTime -= TimeSpan.FromSeconds(20);

            var sendText = JsonSerializer.Serialize(DefaultDeviceInfoStruct);
            var sendBytes = Encoding.UTF8.GetBytes(sendText);

            foreach (var item in SupportedNetworkInterfacesIndexes)
            {
                // 如果错误网络适配器中存在当前项的记录, 跳过
                if (erroredInterfacesIndexes.Contains(item)) continue;

                try
                {
                    UdpSender?.Client.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.MulticastInterface,
                        item
                    );
                    UdpSender?.Send(sendBytes, sendBytes.Length, multicast);

                    while (Messages2BroadCast.Count > 0)
                    {
                        var messageBytes = Encoding.UTF8.GetBytes(Messages2BroadCast.Dequeue());
                        UdpSender?.Send(messageBytes, messageBytes.Length, multicast);
                    } // 将自定义广播消息全部发送
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

            if (CloseDevicesDiscoveryServerRequest)
                CloseDevicesDiscoveryServerRequest = false;

            if (!GlobalInfo.Running || CloseDevicesDiscoveryServerRequest) await Stop();
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
                    if (bytes is null) continue;    //  null byte[] cause exception in next line.
                    var result = Encoding.UTF8.GetString(bytes);
                    var client = $"{multicast.Address}:{multicast.Port}";

                    Log.Information($"UDP From: {client,-21}, Receive: {result}");

                    try
                    {
                        DevicesManager.Update(
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
        DeviceInfoStructUpdatedTimes = 0;
        LastTimeToOSVersionUpdated = 0;
        CloseDevicesDiscoveryServerRequest = false;
        SupportedNetworkInterfacesIndexes.Clear();
        Messages2BroadCast.Clear();
        DefaultDeviceInfoStruct = NetworkHelper.GetDeviceInfo();
    }

    public async Task<DevicesDiscoveryServer> Start()
    {
        Status = ServerStatus.Starting;

        Init();

        UdpSender = new(Program.Config.Web.UDPPortSend, AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
            MulticastLoopback = true
        };

        UdpReceiver = new(
            new IPEndPoint(IPAddress.Any, Program.Config.Web.UDPPortReceive)
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
                    IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress)
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
        Status = ServerStatus.Stopping;

        await Task.Run(() =>
        {
            CloseDevicesDiscoveryServerRequest = true;

            while (CloseDevicesDiscoveryServerRequest) { }

            UdpSendTimer?.Close();

            UdpSender?.Close();
            UdpReceiver?.Close();
        });

        Status = ServerStatus.Pending;

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

    public void Dispose()
    {
        UdpSender?.Dispose();
        UdpReceiver?.Dispose();

        GC.SuppressFinalize(this);
    }
}
