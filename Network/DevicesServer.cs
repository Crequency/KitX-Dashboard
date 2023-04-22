using KitX_Dashboard.Data;
using KitX_Dashboard.Interfaces.Network;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KitX_Dashboard.Network;

internal class DevicesServer : IKitXServer<DevicesServer>
{
    public NetworkType Type { get; set; } = NetworkType.Unknown;

    internal Thread? acceptDeviceThread;
    internal Thread? receiveMessageThread;
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

        EventService.Invoke(nameof(EventService.DevicesServerPortChanged));

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
                                ReceiveMessage(client);
                            }
                            catch (Exception e)
                            {
                                Log.Error(e,
                                    "In DevicesServer.AcceptClient().ReceiveMessageFromHost()");
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
    /// 接收客户端消息
    /// </summary>
    /// <param name="obj">TcpClient</param>
    private async void ReceiveMessage(object obj)
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
            Log.Error($"Error: In ReceiveMessageFromHost() : {ex.Message}");
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
    /// 向所有客户端广播消息
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

            keepListen = true;

            receiveMessageThread = new(ReceiveMessageFromHost);

            receiveMessageThread.Start();

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
    internal void SendMessageToHost(string msg)
    {
        try
        {
            var stream = DevicesHost?.GetStream();

            if (stream is null) return;

            var data = Encoding.UTF8.GetBytes(msg);

            stream.Write(data, 0, data.Length);
            stream.Flush();

            //DevicesHost?.Client.Send(Encoding.UTF8.GetBytes(msg));

            Log.Information($"Sent Message to Host, msg: {msg}");
        }
        catch (Exception e)
        {
            var location = $"{nameof(DevicesServer)}.{nameof(SendMessageToHost)}";

            Log.Error(e, $"In {location}: {e.Message}");

            Program.WebManager?.Restart(restartPluginsServer: false);
        }
    }

    /// <summary>
    /// 从主控接收消息
    /// </summary>
    internal async void ReceiveMessageFromHost()
    {
        if (DevicesHost is null) return;

        var stream = DevicesHost?.GetStream();

        if (stream is null) return;

        var buffer = new byte[Program.Config.Web.SocketBufferSize];  //  Default 10 MB buffer

        try
        {
            while (keepListen)
            {

                if (buffer is null) break;

                var length = await stream.ReadAsync(buffer);

                if (length > 0)
                {
                    var msg = Encoding.UTF8.GetString(buffer, 0, length);

                    Log.Information($"Receive from Host: {msg}");
                }
                else
                {
                    keepListen = false;
                    break;
                }
            }

            stream.Close();
            stream.Dispose();

            Log.Information($"Closing `{nameof(ReceiveMessageFromHost)}` thread.");

            Program.WebManager?.Restart(restartPluginsServer: false);
        }
        catch (Exception e)
        {
            var location = $"{nameof(DevicesServer)}.{nameof(ReceiveMessageFromHost)}";
            Log.Error(e, $"In {location}: {e.Message}");

            stream.Close();
            stream.Dispose();

            DevicesHost?.Close();
            DevicesHost?.Dispose();

            Program.WebManager?.Restart(restartPluginsServer: false);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public async Task<DevicesServer> Broadcast(byte[] content)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesServer> BroadCast(byte[] content, Func<TcpClient, bool>? pattern)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesServer> Send(byte[] content, string target)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesServer> OnReceive(Action<byte[], string> action)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesServer> Start()
    {
        await TasksManager.RunTaskAsync(() =>
        {
            DevicesHost = new();
        }, $"{nameof(DevicesServer)}.{nameof(Start)}");

        return this;
    }

    public async Task<DevicesServer> Stop()
    {
        await Task.Run(() =>
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
        });

        return this;
    }

    public async Task<DevicesServer> Restart()
    {
        await Task.Run(async () =>
        {
            await Start();
            await Stop();
        });

        return this;
    }
}
