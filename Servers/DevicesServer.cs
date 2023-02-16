using Common.BasicHelper.Core.Shell;
using Common.BasicHelper.Util.Extension;
using KitX.Web.Rules;
using KitX_Dashboard.Converters;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Names;
using KitX_Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace KitX_Dashboard.Servers;

internal class DevicesServer : IDisposable
{

    public DevicesServer()
    {

    }

    public void Start()
    {
        new Thread(() =>
        {
            try
            {
                //  寻找所有支持的网络适配器
                Log.Information($"Start {nameof(FindSupportNetworkInterfaces)}");
                try
                {
                    FindSupportNetworkInterfaces(new()
                    {
                        UdpClient_Send, UdpClient_Receive
                    }, IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress));
                }
                catch (Exception ex)
                {
                    var location = $"{nameof(DevicesServer)}.{nameof(Start)}";
                    Log.Warning(ex, $"In {location}: {ex.Message}");
                }

                #region 组播收发消息

                //  开始组播发送本机信息
                Log.Information($"Start {nameof(MultiDevicesBroadCastSend)}");
                MultiDevicesBroadCastSend();

                //  开始接收组播消息
                Log.Information($"Start {nameof(MultiDevicesBroadCastReceive)}");
                MultiDevicesBroadCastReceive();

                #endregion
            }
            catch (Exception e)
            {
                var location = $"{nameof(DevicesServer)}.{nameof(Start)}()";
                Log.Error(e, $"In {location}: {e.Message}");
            }

            try
            {
                Log.Information($"Start Init {nameof(DevicesHost)}");
                //  初始化自组网
                DevicesHost = new();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {nameof(DevicesServer)}, " +
                    $"Init {nameof(DevicesHost)}");
            }
        }).Start();
    }

    public void Stop()
    {
        keepListen = false;

        foreach (KeyValuePair<string, TcpClient> item in clients)
        {
            item.Value.Close();
            item.Value.Dispose();
        }

        acceptDeviceThread?.Join();

        DevicesHost?.Close();
        DevicesHost?.Dispose();
    }

    #region UDP Socket 服务于自发现自组网

    private static readonly List<int> SurpportedNetworkInterfaces = new();

    internal static readonly Queue<string> Messages2BroadCast = new();

    internal static DeviceInfoStruct DefaultDeviceInfoStruct = GetDeviceInfo();

    /// <summary>
    /// UDP 发包客户端
    /// </summary>
    private static readonly UdpClient UdpClient_Send
        = new(Program.Config.Web.UDPPortSend, AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
            MulticastLoopback = true
        };

    /// <summary>
    /// UDP 收包客户端
    /// </summary>
    private static readonly UdpClient UdpClient_Receive
        = new(new IPEndPoint(IPAddress.Any, Program.Config.Web.UDPPortReceive));

    /// <summary>
    /// 检查网络适配器是否符合要求
    /// </summary>
    /// <param name="adapter">网络适配器</param>
    /// <returns>是否符合要求</returns>
    private static bool CheckNetworkInterface(
        NetworkInterface adapter, IPInterfaceProperties adapterProperties)
    {
        var userPointed = Program.Config.Web.AcceptedNetworkInterfaces;
        if (userPointed is not null && userPointed.Contains(adapter.Name))
            return true;

        if (

                adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211

            &&
            (
                adapterProperties.MulticastAddresses.Count == 0 ||
                // most of VPN adapters will be skipped
                !adapter.SupportsMulticast ||
                // multicast is meaningless for this type of connection
                OperationalStatus.Up != adapter.OperationalStatus ||
                // this adapter is off or not connected
                !adapter.Supports(NetworkInterfaceComponent.IPv4)
            )
            ) return false;
        return true;
    }

    /// <summary>
    /// 寻找受支持的网络适配器并把UDP客户端加入组播
    /// </summary>
    private static void FindSupportNetworkInterfaces(List<UdpClient> clients, IPAddress multicastAddress)
    {
        var multicastGroupJoinedInterfacesCount = 0;

        NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
        foreach (NetworkInterface adapter in nics)
        {
            IPInterfaceProperties adapterProperties = adapter.GetIPProperties();

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
            if (!CheckNetworkInterface(adapter, adapterProperties)) continue;
            UnicastIPAddressInformationCollection unicastIPAddresses
                = adapterProperties.UnicastAddresses;
            IPv4InterfaceProperties p = adapterProperties.GetIPv4Properties();
            if (p is null) continue;    // IPv4 is not configured on this adapter
            SurpportedNetworkInterfaces.Add(IPAddress.HostToNetworkOrder(p.Index));
            IPAddress? ipAddress = null;
            foreach (UnicastIPAddressInformation unicastIPAddress in unicastIPAddresses)
            {
                if (unicastIPAddress.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                ipAddress = unicastIPAddress.Address;
                break;
            }
            if (ipAddress is null) continue;
            try
            {
                foreach (var udpClient in clients)
                    udpClient.JoinMulticastGroup(multicastAddress, ipAddress);
                Program.WebManager?.NetworkInterfaceRegistered?.Add(adapter.Name);
                ++multicastGroupJoinedInterfacesCount;
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    $"In {nameof(DevicesServer)}.{nameof(FindSupportNetworkInterfaces)}:" +
                    $"{ex.Message}");
            }
        }

        Program.TasksManager?.RaiseSignal(nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal));

        Log.Information($"" +
            $"Find {SurpportedNetworkInterfaces.Count} supported network interfaces.");
        Log.Information($"" +
            $"Joined {multicastGroupJoinedInterfacesCount} multicast groups.");
    }

    /// <summary>
    /// 多设备广播发送方法
    /// </summary>
    public static void MultiDevicesBroadCastSend()
    {
        #region 初始化 UDP 客户端

        UdpClient udpClient = UdpClient_Send;
        IPEndPoint multicast =
            new(IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress),
            Program.Config.Web.UDPPortReceive);
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress, true);

        #endregion

        var erroredInterfacesIndexes = new List<int>();
        var erroredInterfacesIndexesTTL = 60;

        System.Timers.Timer timer = new()
        {
            Interval = Program.Config.Web.UDPSendFrequency,
            AutoReset = true
        };
        void EndMultiDevicesBroadCastSend()
        {
            timer.Stop();
            timer.Dispose();
            udpClient.Close();
            udpClient.Dispose();
        }
        timer.Elapsed += (_, _) =>
        {
            try
            {
                --erroredInterfacesIndexesTTL;
                if (erroredInterfacesIndexesTTL <= 0)
                {
                    erroredInterfacesIndexesTTL = 60;
                    erroredInterfacesIndexes.Clear();
                }

                UpdateDefaultDeviceInfoStruct();
                string sendText = JsonSerializer.Serialize(DefaultDeviceInfoStruct);
                byte[] sendBytes = Encoding.UTF8.GetBytes(sendText);

                foreach (var item in SurpportedNetworkInterfaces)
                {
                    //  如果错误网络适配器中存在当前项的记录, 跳过
                    if (erroredInterfacesIndexes.Contains(item)) continue;

                    try
                    {
                        udpClient.Client.SetSocketOption(SocketOptionLevel.IP,
                            SocketOptionName.MulticastInterface, item);
                        udpClient.Send(sendBytes, sendBytes.Length, multicast);

                        //  将自定义广播消息全部发送
                        while (Messages2BroadCast.Count > 0)
                        {
                            byte[] messageBytes = Encoding.UTF8.GetBytes(Messages2BroadCast.Dequeue());
                            udpClient.Send(messageBytes, messageBytes.Length, multicast);
                        }
                    }
                    catch (Exception ex)
                    {
                        //  该网络适配器存在异常, 暂时记录到错误网络适配器中
                        if (!erroredInterfacesIndexes.Contains(item))
                            erroredInterfacesIndexes.Add(item);

                        var location = $"{nameof(DevicesServer)}.{nameof(MultiDevicesBroadCastSend)}";
                        Log.Warning(ex, $"In {location}: {ex.Message} - " +
                            $"On interface index: {item}, recorded.");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, $"In MultiDevicesBroadCastSend: {e.Message}");
            }
            if (!GlobalInfo.Running) EndMultiDevicesBroadCastSend();
        };
        timer.Start();
    }

    /// <summary>
    /// 多设备广播接收方法
    /// </summary>
    public static void MultiDevicesBroadCastReceive()
    {
        #region 初始化 UDP 客户端

        UdpClient udpClient = UdpClient_Receive;
        IPEndPoint multicast = new(IPAddress.Any, 0);
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress, true);

        #endregion

        new Thread(() =>
        {
            try
            {
                while (GlobalInfo.Running)
                {
                    var bytes = udpClient.Receive(ref multicast);
                    if (bytes is null) continue;    //  null byte[] cause exception in next line.
                    var result = Encoding.UTF8.GetString(bytes);
                    if (result is null) continue;   //  null string skip.
                    var client = $"{multicast.Address}:{multicast.Port}";
                    Log.Information($"UDP " +
                        $"From: {client,-21}, " +
                        $"Receive: {result}");
                    try
                    {
                        DeviceInfoStruct deviceInfo
                            = JsonSerializer.Deserialize<DeviceInfoStruct>(result);
                        DevicesManager.Update(deviceInfo);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"{ex.Message}");
                    }
                }
                udpClient.Close();
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
            if (!GlobalInfo.Running)
            {
                udpClient.Close();
            }
            else
            {

            }
        }).Start();
    }

    /// <summary>
    /// 将 IPv4 的十进制表示按点分制拆分
    /// </summary>
    /// <param name="ip">IPv4 的十进制表示</param>
    /// <returns>拆分</returns>
    private static (int, int, int, int) IPv4_2_4Parts(string ip)
    {
        string[] p = ip.Split('.');
        int a = int.Parse(p[0]), b = int.Parse(p[1]), c = int.Parse(p[2]), d = int.Parse(p[3]);
        return (a, b, c, d);
    }

    /// <summary>
    /// 获取本机内网 IPv4 地址
    /// </summary>
    /// <returns>使用点分十进制表示法的本机内网IPv4地址</returns>
    private static string GetInterNetworkIPv4()
    {
        try
        {
            return (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    where ip.AddressFamily == AddressFamily.InterNetwork
                        && !ip.ToString().Equals("127.0.0.1")
                        && (ip.ToString().StartsWith("192.168")                         //  192.168.x.x
                            || ip.ToString().StartsWith("10")                           //  10.x.x.x
                            || IPv4_2_4Parts(ip.ToString()).Item1 == 172               //  172.16-31.x.x
                                && IPv4_2_4Parts(ip.ToString()).Item2 >= 16
                                && IPv4_2_4Parts(ip.ToString()).Item2 <= 31)
                        && ip.ToString().StartsWith(Program.Config.Web.IPFilter)  //  满足自定义规则
                    select ip).First().ToString();
        }
        catch (Exception ex)
        {
            var location = $"{nameof(DevicesServer)}.{nameof(GetInterNetworkIPv4)}";
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 获取本机内网 IPv6 地址
    /// </summary>
    /// <returns>IPv6 地址</returns>
    private static string GetInterNetworkIPv6()
    {
        try
        {
            return (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    where ip.AddressFamily == AddressFamily.InterNetworkV6
                        && !ip.ToString().Equals("::1")
                    select ip).First().ToString();
        }
        catch (Exception ex)
        {
            var location = $"{nameof(DevicesServer)}.{nameof(GetInterNetworkIPv6)}";
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 尝试获取设备 MAC 地址
    /// </summary>
    /// <returns>MAC 地址</returns>
    private static string? TryGetDeviceMacAddress()
    {
        try
        {
            string? result = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(nic => nic.GetPhysicalAddress().ToString()).FirstOrDefault();
            return result;
        }
        catch (Exception ex)
        {
            var location = $"{nameof(DevicesServer)}.{nameof(TryGetDeviceMacAddress)}";
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 尝试获取系统版本
    /// </summary>
    /// <returns>系统版本</returns>
    private static string? TryGetOSVersionString()
    {
        var result = Environment.OSVersion.VersionString;
        try
        {
            switch (OperatingSystem2Enum.GetOSType())
            {
                case OperatingSystems.Linux:

                    var versionFilePath = "/etc/os-release";
                    var versionSegament = "PRETTY_NAME";
                    var needFindInIssue = false;

                    if (File.Exists(versionFilePath))
                    {
                        var osRelease = File.ReadAllLines(versionFilePath)
                            .Select(line => line.Split('='))
                            .ToDictionary
                            (
                                parts => parts[0],
                                parts => parts[1].Trim('"')
                            );

                        if (osRelease.TryGetValue(versionSegament, out var version))
                            result = version;
                        else needFindInIssue = true;
                    }

                    if (needFindInIssue)
                    {
                        var issueFilePath = "/etc/issue";
                        if (File.Exists(issueFilePath))
                        {
                            var issue = File.ReadAllText(issueFilePath);
                            var lines = issue.Split('\n');
                            result = lines.First(x => !x.Equals(string.Empty));
                        }
                    }

                    break;
                case OperatingSystems.MacOS:
                    var command = "sw_vers";

                    var productName = command.ExecuteAsCommand("-productName");
                    var productVersion = command.ExecuteAsCommand("-productVersion");
                    var buildVersion = command.ExecuteAsCommand("-buildVersion");

                    if (productName is not null && productVersion is not null && buildVersion is not null)
                        result = $"{productName} {productVersion} {buildVersion}"
                            .Replace("\n", "");

                    break;
            }
        }
        catch (Exception ex)
        {
            var location = $"{nameof(DevicesServer)}.{nameof(TryGetOSVersionString)}";
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// 获取设备信息
    /// </summary>
    /// <returns>设备信息结构体</returns>
    private static DeviceInfoStruct GetDeviceInfo() => new()
    {
        DeviceName = Environment.MachineName,
        DeviceMacAddress = TryGetDeviceMacAddress(),
        IsMainDevice = GlobalInfo.IsMainMachine,
        SendTime = DateTime.UtcNow,
        DeviceOSType = OperatingSystem2Enum.GetOSType(),
        DeviceOSVersion = TryGetOSVersionString(),
        IPv4 = GetInterNetworkIPv4(),
        IPv6 = GetInterNetworkIPv6(),
        PluginServerPort = GlobalInfo.PluginServerPort,
        DeviceServerPort = GlobalInfo.DeviceServerPort,
        DeviceServerBuildTime = new(),
        PluginsCount = Program.PluginCards.Count,
    };

    private static int DeviceInfoStructUpdatedTimes = 0;

    private static int LastTimeToOSVersionUpdated = 0;

    /// <summary>
    /// 更新默认设备信息结构
    /// </summary>
    private static void UpdateDefaultDeviceInfoStruct()
    {
        DefaultDeviceInfoStruct.IsMainDevice = GlobalInfo.IsMainMachine;
        DefaultDeviceInfoStruct.SendTime = DateTime.UtcNow;
        DefaultDeviceInfoStruct.IPv4 = GetInterNetworkIPv4();
        DefaultDeviceInfoStruct.IPv6 = GetInterNetworkIPv6();
        DefaultDeviceInfoStruct.PluginServerPort = GlobalInfo.PluginServerPort;
        DefaultDeviceInfoStruct.PluginsCount = Program.PluginCards.Count;
        DefaultDeviceInfoStruct.IsMainDevice = GlobalInfo.IsMainMachine;
        DefaultDeviceInfoStruct.DeviceServerPort = GlobalInfo.DeviceServerPort;
        DefaultDeviceInfoStruct.DeviceServerBuildTime = GlobalInfo.ServerBuildTime;

        if (LastTimeToOSVersionUpdated > Program.Config.IO.OperatingSystemVersionUpdateInterval)
        {
            LastTimeToOSVersionUpdated = 0;
            DefaultDeviceInfoStruct.DeviceOSVersion = TryGetOSVersionString();
        }

        ++DeviceInfoStructUpdatedTimes;
        ++LastTimeToOSVersionUpdated;

        if (DeviceInfoStructUpdatedTimes < 0) DeviceInfoStructUpdatedTimes = 0;
    }

    #endregion

    #region TCP Socket 服务于设备间组网

    internal Thread? acceptDeviceThread;
    internal TcpClient? DevicesHost;
    internal TcpListener? listener;
    internal bool keepListen = true;

    public readonly Dictionary<string, TcpClient> clients = new();

    /// <summary>
    /// 建立主控网络
    /// </summary>
    internal void BuildServer()
    {
        listener = new(IPAddress.Any, 0);
        acceptDeviceThread = new(AcceptClient);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port; // 取服务端口号

        GlobalInfo.DeviceServerPort = port; // 全局端口号标明
        GlobalInfo.ServerBuildTime = DateTime.UtcNow;
        GlobalInfo.IsMainMachine = true;

        Log.Information($"DevicesServer Port: {port}");

        acceptDeviceThread.Start();

        EventService.Invoke(nameof(EventService.DevicesServerPortChanged));
    }

    /// <summary>
    /// 取消建立主控网络
    /// </summary>
    internal void CancleBuildServer()
    {
        GlobalInfo.IsMainMachine = false;
        GlobalInfo.DeviceServerPort = -1;

        keepListen = false;
        acceptDeviceThread?.Join();

        DevicesHost?.Close();
        DevicesHost?.Dispose();

        DevicesManager.Watch4MainDevice();  //  取消建立之后重新寻找并加入主控网络
    }

    /// <summary>
    /// 接收客户端
    /// </summary>
    internal void AcceptClient()
    {
        try
        {
            while (keepListen)
            {
                if (listener != null && listener.Pending())
                {
                    TcpClient client = listener.AcceptTcpClient();
                    if (client.Client.RemoteEndPoint is IPEndPoint endpoint)
                    {
                        clients.Add(endpoint.ToString(), client);

                        Log.Information($"New device connection: {endpoint}");

                        // 新建并运行接收消息线程
                        new Thread(() =>
                        {
                            try
                            {
                                ReciveMessage(client);
                            }
                            catch (Exception e)
                            {
                                Log.Error(e,
                                    "In DevicesServer.AcceptClient().ReciveMessage()");
                            }
                        }).Start();
                    }
                }
                else
                {
                    Thread.Sleep(100);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"In AcceptClient() : {ex.Message}");
        }
    }

    /// <summary>
    /// 接收消息
    /// </summary>
    /// <param name="obj">TcpClient</param>
    private async void ReciveMessage(object obj)
    {
        TcpClient? client = obj as TcpClient;
        IPEndPoint? endpoint = null;
        NetworkStream? stream = null;

        try
        {
            endpoint = client?.Client.RemoteEndPoint as IPEndPoint;
            stream = client?.GetStream();

            while (keepListen && stream != null)
            {
                byte[] data = new byte[Program.Config.Web.SocketBufferSize];
                //如果远程主机已关闭连接,Read将立即返回零字节
                //int length = await stream.ReadAsync(data, 0, data.Length);
                int length = await stream.ReadAsync(data);
                if (length > 0)
                {
                    string msg = Encoding.UTF8.GetString(data, 0, length);

                    Log.Information($"From: {endpoint}\tReceive: {msg}");


                    //发送到其他客户端
                    //foreach (KeyValuePair<string, TcpClient> kvp in clients)
                    //{
                    //    if (kvp.Value != client)
                    //    {
                    //        byte[] writeData = Encoding.UTF8.GetBytes(msg);
                    //        NetworkStream writeStream = kvp.Value.GetStream();
                    //        writeStream.Write(writeData, 0, writeData.Length);
                    //    }
                    //}
                }
                else
                {

                    break; //客户端断开连接 跳出循环
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error: In ReciveMessage() : {ex.Message}");
            Log.Information($"Connection broke from: {endpoint}");

            //Read是阻塞方法 客户端退出是会引发异常 释放资源 结束此线程
        }
        finally
        {
            //释放资源
            if (endpoint != null)
            {
                clients.Remove(endpoint.ToString());
            }
            stream?.Close();
            stream?.Dispose();
            client?.Dispose();
        }
    }

    /// <summary>
    /// 向客户端发送消息
    /// </summary>
    /// <param name="msg">消息内容</param>
    /// <param name="client">客户端</param>
    internal void SendMessage(string msg, string client)
    {
        if (clients.ContainsKey(client))
            clients[client].Client.Send(Encoding.UTF8.GetBytes(msg));
    }

    /// <summary>
    /// 广播消息
    /// </summary>
    /// <param name="msg">消息</param>
    internal void BroadCastMessage(string msg, Func<TcpClient, bool>? pattern)
    {
        foreach (var client in clients)
        {
            if (pattern is not null && pattern.Invoke(client.Value))
                client.Value.Client.Send(Encoding.UTF8.GetBytes(msg));
            else client.Value.Client.Send(Encoding.UTF8.GetBytes(msg));
        }
    }

    /// <summary>
    /// 加入主控网络
    /// </summary>
    /// <param name="serverAddress">主控地址</param>
    /// <param name="serverPort">主控端口</param>
    internal void AttendServer(string serverAddress, int serverPort)
    {
        try
        {
            DevicesHost?.Connect(serverAddress, serverPort);
            Log.Information($"Attending Server -> {serverAddress}:{serverPort}");
        }
        catch (Exception ex)
        {
            var location = $"{nameof(DevicesServer)}.{nameof(AttendServer)}";
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    /// <summary>
    /// 向主控发送消息
    /// </summary>
    /// <param name="msg">消息内容</param>
    internal void SendMessage(string msg)
    {
        try
        {
            DevicesHost?.Client.Send(Encoding.UTF8.GetBytes(msg));
            Log.Information($"Sent Message to Host, msg: {msg}");
        }
        catch (Exception e)
        {
            var location = $"{nameof(DevicesServer)}.{nameof(SendMessage)}";
            Log.Error(e, $"In {location}: {e.Message}");
        }
    }

    #endregion

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

}
