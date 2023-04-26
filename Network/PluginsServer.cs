using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Data;
using KitX_Dashboard.Interfaces.Network;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KitX_Dashboard.Network;

internal class PluginsServer : IKitXServer<PluginsServer>
{
    private static TcpListener? listener = null;

    private static bool keepListen = true;

    private static readonly Dictionary<string, TcpClient> clients = new();

    private static Action<byte[], int?, string>? onReceive = null;

    public static ServerStatus? Status { get; set; } = ServerStatus.Unknown;

    /// <summary>
    /// 接收客户端
    /// </summary>
    private static void AcceptClient()
    {
        var location = $"{nameof(PluginsServer)}.{nameof(AcceptClient)}";

        try
        {
            while (keepListen && listener is not null)
            {
                if (listener.Pending())
                {
                    var client = listener.AcceptTcpClient();

                    if (client.Client.RemoteEndPoint is not IPEndPoint endpoint) continue;

                    clients.Add(endpoint.ToString(), client);

                    Log.Information($"New plugin connection: {endpoint}");

                    ReceiveMessage(client);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    /// <summary>
    /// 接收消息
    /// </summary>
    /// <param name="obj">TcpClient</param>
    private static void ReceiveMessage(TcpClient client)
    {
        var location = $"{nameof(PluginsServer)}.{nameof(ReceiveMessage)}";

        IPEndPoint? endpoint = null;
        NetworkStream? stream = null;

        new Thread(async () =>
        {
            try
            {
                endpoint = client.Client.RemoteEndPoint as IPEndPoint;

                stream = client.GetStream();

                if (endpoint is null || stream is null) return;

                while (keepListen)
                {
                    var buffer = new byte[Program.Config.Web.SocketBufferSize];

                    var length = stream is null ? 0 : await stream.ReadAsync(buffer);

                    if (length > 0)
                    {
                        onReceive?.Invoke(buffer, length, endpoint.ToString());

                        var msg = buffer.ToUTF8(0, length);

                        Log.Information($"From: {endpoint}\tReceive: {msg}");

                        if (msg.StartsWith("PluginStruct: "))
                        {
                            PluginsNetwork.Execute(msg[14..], endpoint);

                            var workPath = Program.Config.App.LocalPluginsDataFolder.GetFullPath();
                            var sendtxt = $"WorkPath: {workPath}";
                            var bytes = sendtxt.FromUTF8();

                            stream?.Write(bytes, 0, bytes.Length);
                        }

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
                    else break; //客户端断开连接 跳出循环
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
                Log.Information($"Connection broke from: {endpoint}");
            }
            finally
            {
                if (endpoint is not null)
                {
                    PluginsNetwork.Disconnect(endpoint);

                    clients.Remove(endpoint.ToString());
                }

                stream?.CloseAndDispose();

                client.Dispose();
            }
        }).Start();
    }

    private static void Init()
    {
        clients.Clear();

        var port = Program.Config.Web.UserSpecifiedPluginsServerPort;

        if (port < 0 || port > 65535) port = null;

        listener = new(IPAddress.Any, port ?? 0);
    }

    public async Task<PluginsServer> Broadcast(byte[] content)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<PluginsServer> BroadCast(byte[] content, Func<TcpClient, bool>? pattern)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<PluginsServer> Send(byte[] content, string target)
    {
        await Task.Run(() => { });

        return this;
    }

    public PluginsServer OnReceive(Action<byte[], int?, string> action)
    {
        onReceive = action;

        return this;
    }

    public async Task<PluginsServer> Start()
    {
        await TasksManager.RunTaskAsync(() =>
        {
            Init();

            if (listener is null) return;

            listener.Start();

            var port = ((IPEndPoint)listener.LocalEndpoint).Port; // 取服务端口号

            GlobalInfo.PluginServerPort = port; // 全局端口号标明

            EventService.Invoke(nameof(EventService.PluginsServerPortChanged));

            Log.Information($"PluginsServer Port: {port}");

            new Thread(AcceptClient).Start();
        });

        return this;
    }

    public async Task<PluginsServer> Stop()
    {
        keepListen = false;

        await Task.Run(() =>
        {
            foreach (KeyValuePair<string, TcpClient> item in clients)
            {
                item.Value.Close();
                item.Value.Dispose();
            }
        });

        return this;
    }

    public async Task<PluginsServer> Restart()
    {
        await Task.Run(() => { });

        return this;
    }

    public void Dispose()
    {
        keepListen = false;

        listener?.Stop();

        GC.SuppressFinalize(this);
    }
}
