using KitX.Web.Rules;
using KitX_Dashboard.Converters;
using KitX_Dashboard.Data;
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

namespace KitX_Dashboard.Services
{
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
                    Log.Information($"Start {nameof(FindSurpportNetworkInterface)}");
                    //  寻找所有支持的网络适配器
                    FindSurpportNetworkInterface(new()
                    {
                        UdpClient_Send, UdpClient_Receive
                    }, IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress));
                }
                catch (Exception ex)
                {
                    Log.Error($"In {nameof(DevicesServer)}, " +
                        $"{nameof(FindSurpportNetworkInterface)}", ex);
                }
                try
                {
                    Log.Information($"Start {nameof(MultiDevicesBroadCastSend)}");
                    //  开始组播发送本机信息
                    MultiDevicesBroadCastSend();
                }
                catch (Exception ex)
                {
                    Log.Error($"In {nameof(DevicesServer)}, " +
                        $"{nameof(MultiDevicesBroadCastSend)}", ex);
                }
                try
                {
                    Log.Information($"Start {nameof(MultiDevicesBroadCastReceive)}");
                    //  开始接收组播消息
                    MultiDevicesBroadCastReceive();
                }
                catch (Exception ex)
                {
                    Log.Error($"In {nameof(DevicesServer)}, " +
                        $"{nameof(MultiDevicesBroadCastReceive)}", ex);
                }
                try
                {
                    Log.Information($"Start Init {nameof(DevicesHost)}");
                    //  初始化自组网
                    DevicesHost = new();
                }
                catch (Exception ex)
                {
                    Log.Error($"In {nameof(DevicesServer)}, " +
                        $"Init {nameof(DevicesHost)}", ex);
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
        /// 寻找受支持的网络适配器并把UDP客户端加入组播
        /// </summary>
        private static void FindSurpportNetworkInterface(List<UdpClient> clients, IPAddress multicastAddress)
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                IPInterfaceProperties ip_properties = adapter.GetIPProperties();
                if (adapter.GetIPProperties().MulticastAddresses.Count == 0
                    // most of VPN adapters will be skipped
                    || !adapter.SupportsMulticast
                    // multicast is meaningless for this type of connection
                    || OperationalStatus.Up != adapter.OperationalStatus
                    // this adapter is off or not connected
                    || !adapter.Supports(NetworkInterfaceComponent.IPv4)
                    ) continue;
                IPInterfaceProperties adapterProperties = adapter.GetIPProperties();
                UnicastIPAddressInformationCollection unicastIPAddresses
                    = adapterProperties.UnicastAddresses;
                IPv4InterfaceProperties p = adapterProperties.GetIPv4Properties();
                if (p == null) continue;    // IPv4 is not configured on this adapter
                SurpportedNetworkInterfaces.Add(IPAddress.HostToNetworkOrder(p.Index));
                IPAddress? ipAddress = null;
                foreach (UnicastIPAddressInformation unicastIPAddress in unicastIPAddresses)
                {
                    if (unicastIPAddress.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    ipAddress = unicastIPAddress.Address;
                    break;
                }
                if (ipAddress == null) continue;
                foreach (var udpClient in clients)
                    udpClient.JoinMulticastGroup(multicastAddress, ipAddress);
            }
        }

        /// <summary>
        /// 多设备广播发送方法
        /// </summary>
        public static void MultiDevicesBroadCastSend()
        {
            #region 初始化 UDP 客户端

            UdpClient udpClient = UdpClient_Send;
            IPEndPoint multicast = new(IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress),
                Program.Config.Web.UDPPortReceive);
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress, true);

            #endregion

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
                    UpdateDefaultDeviceInfoStruct();
                    string sendText = JsonSerializer.Serialize(DefaultDeviceInfoStruct);
                    byte[] sendBytes = Encoding.UTF8.GetBytes(sendText);

                    foreach (var item in SurpportedNetworkInterfaces)
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
                }
                catch (Exception e)
                {
                    Log.Error($"In MultiDevicesBroadCastSend: {e.Message}", e);
                    try
                    {
                        Log.Information($"Start {nameof(MultiDevicesBroadCastSendDefault)}");
                        EndMultiDevicesBroadCastSend();
                        //  组播发送失败, 尝试由系统决定发送的网络适配器
                        MultiDevicesBroadCastSendDefault();
                    }
                    catch (Exception exc)
                    {
                        Log.Error($"In {nameof(DevicesServer)}, " +
                            $"{nameof(MultiDevicesBroadCastSendDefault)}", exc);
                    }
                }
                if (!GlobalInfo.Running) EndMultiDevicesBroadCastSend();
            };
            timer.Start();
        }

        /// <summary>
        /// 默认的多设备组播发送方法
        /// </summary>
        public static void MultiDevicesBroadCastSendDefault()
        {
            #region 初始化 UDP 客户端

            UdpClient udpClient = new(Program.Config.Web.UDPPortSend);
            udpClient.JoinMulticastGroup(IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress));
            IPEndPoint multicast = new(IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress),
                Program.Config.Web.UDPPortReceive);

            #endregion

            System.Timers.Timer timer = new()
            {
                Interval = Program.Config.Web.UDPSendFrequency,
                AutoReset = true
            };
            void EndMultiDevicesBroadCastSendDefault()
            {
                udpClient.Close();
                udpClient.Dispose();
                timer.Stop();
                timer.Dispose();
            }
            timer.Elapsed += (_, _) =>
            {
                try
                {
                    UpdateDefaultDeviceInfoStruct();
                    string sendText = JsonSerializer.Serialize(DefaultDeviceInfoStruct);
                    byte[] sendBytes = Encoding.UTF8.GetBytes(sendText);
                    udpClient.Send(sendBytes, sendBytes.Length, multicast);

                    //  将自定义广播消息全部发送
                    while (Messages2BroadCast.Count > 0)
                    {
                        byte[] messageBytes = Encoding.UTF8.GetBytes(Messages2BroadCast.Dequeue());
                        udpClient.Send(messageBytes, messageBytes.Length, multicast);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"In MultiDevicesBroadCastSend: {e.Message}");
                    EndMultiDevicesBroadCastSendDefault();
                    //  默认发包方式发生问题, 尝试遍历适配器发送报文
                    MultiDevicesBroadCastSend();
                }
                if (!GlobalInfo.Running) EndMultiDevicesBroadCastSendDefault();
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
                        byte[] bytes = udpClient.Receive(ref multicast);
                        string result = Encoding.UTF8.GetString(bytes);
                        Log.Information($"UDP Receive: {result}");
                        try
                        {
                            DeviceInfoStruct deviceInfo = JsonSerializer.Deserialize<DeviceInfoStruct>(result);
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
                    Log.Error(e.Message, e);
                }
                if (!GlobalInfo.Running)
                {
                    udpClient.Close();
                }
                else
                {
                    try
                    {
                        Log.Information($"Start {nameof(MultiDevicesBroadCastReceiveDefault)}");
                        //  组播接收失败, 尝试由系统决定接收的网络适配器
                        MultiDevicesBroadCastReceiveDefault();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("In MultiDevicesBroadCastReceiveDefault()", ex);
                    }
                }
            }).Start();
        }

        /// <summary>
        /// 默认的多设备组播接收方法
        /// </summary>
        public static void MultiDevicesBroadCastReceiveDefault()
        {
            UdpClient udpClient = new(Program.Config.Web.UDPPortReceive);
            udpClient.JoinMulticastGroup(IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress));
            IPEndPoint multicast = new(IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress),
                Program.Config.Web.UDPPortSend);
            new Thread(() =>
            {
                try
                {
                    while (GlobalInfo.Running)
                    {
                        byte[] bytes = udpClient.Receive(ref multicast);
                        string result = Encoding.UTF8.GetString(bytes);
                        Log.Information($"UDP Receive: {result}");
                        try
                        {
                            DeviceInfoStruct deviceInfo = JsonSerializer.Deserialize<DeviceInfoStruct>(result);
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
                    Log.Error(e.Message);
                }
                if (!GlobalInfo.Running)
                {
                    udpClient.Close();
                }
                else
                {
                    try
                    {
                        Log.Information($"Start {nameof(MultiDevicesBroadCastReceive)}");
                        //  组播接收失败, 尝试逐个适配器接收消息
                        MultiDevicesBroadCastReceive();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("In MultiDevicesBroadCastReceive()", ex);
                    }
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
            return (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    where ip.AddressFamily == AddressFamily.InterNetwork
                        && !ip.ToString().Equals("127.0.0.1")
                        && (ip.ToString().StartsWith("192.168")                         //  192.168.x.x
                            || ip.ToString().StartsWith("10")                           //  10.x.x.x
                            || (IPv4_2_4Parts(ip.ToString()).Item1 == 172               //  172.16-31.x.x
                                && IPv4_2_4Parts(ip.ToString()).Item2 >= 16
                                && IPv4_2_4Parts(ip.ToString()).Item2 <= 31))
                        && ip.ToString().StartsWith(Program.Config.Web.IPFilter)  //  满足自定义规则
                    select ip).First().ToString();
        }

        /// <summary>
        /// 获取设备信息
        /// </summary>
        /// <returns>设备信息结构体</returns>
        private static DeviceInfoStruct GetDeviceInfo() => new()
        {
            DeviceName = Environment.MachineName,
            DeviceMacAddress = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(nic => nic.GetPhysicalAddress().ToString()).FirstOrDefault(),
            IsMainDevice = GlobalInfo.IsMainMachine,
            SendTime = DateTime.Now,
            DeviceOSType = OperatingSystem2Enum.GetOSType(),
            DeviceOSVersion = Environment.OSVersion.VersionString,
            IPv4 = GetInterNetworkIPv4(),
            IPv6 = (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    where ip.AddressFamily == AddressFamily.InterNetworkV6
                        && !ip.ToString().Equals("::1")
                    select ip).First().ToString(),
            PluginServerPort = GlobalInfo.PluginServerPort,
            DeviceServerPort = GlobalInfo.DeviceServerPort,
            DeviceServerBuildTime = new(),
            PluginsCount = Program.PluginCards.Count,
        };

        /// <summary>
        /// 更新默认设备信息结构
        /// </summary>
        private static void UpdateDefaultDeviceInfoStruct()
        {
            DefaultDeviceInfoStruct.IsMainDevice = GlobalInfo.IsMainMachine;
            DefaultDeviceInfoStruct.SendTime = DateTime.Now;
            DefaultDeviceInfoStruct.DeviceOSVersion = Environment.OSVersion.VersionString;
            DefaultDeviceInfoStruct.IPv4 = GetInterNetworkIPv4();
            DefaultDeviceInfoStruct.IPv6 = (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                                            where ip.AddressFamily == AddressFamily.InterNetworkV6
                                                && !ip.ToString().Equals("::1")
                                            select ip).First().ToString();
            DefaultDeviceInfoStruct.PluginServerPort = GlobalInfo.PluginServerPort;
            DefaultDeviceInfoStruct.PluginsCount = Program.PluginCards.Count;
            DefaultDeviceInfoStruct.IsMainDevice = GlobalInfo.IsMainMachine;
            DefaultDeviceInfoStruct.DeviceServerPort = GlobalInfo.DeviceServerPort;
            DefaultDeviceInfoStruct.DeviceServerBuildTime = GlobalInfo.ServerBuildTime;
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

            int port = ((IPEndPoint)listener.LocalEndpoint).Port; // 取服务端口号
            GlobalInfo.DeviceServerPort = port; // 全局端口号标明
            GlobalInfo.ServerBuildTime = DateTime.Now;
            GlobalInfo.IsMainMachine = true;

            Log.Information($"DevicesServer Port: {port}");

            acceptDeviceThread.Start();

            EventHandlers.Invoke(nameof(EventHandlers.DevicesServerPortChanged));
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
                                    Log.Error("In DevicesServer.AcceptClient()" +
                                        ".ReciveMessage()", e);
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
        /// 加入主控网络
        /// </summary>
        /// <param name="serverAddress">主控地址</param>
        /// <param name="serverPort">主控端口</param>
        internal void AttendServer(string serverAddress, int serverPort)
        {
            Log.Information($"Attending Server -> {serverAddress}:{serverPort}");
            DevicesHost?.Connect(serverAddress, serverPort);
        }

        #endregion

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

    }
}
