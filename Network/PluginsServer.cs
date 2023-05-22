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

    private static bool disposed = false;

    private static readonly Dictionary<string, TcpClient> clients = new();

    private static Action<byte[], int?, string>? onReceive = null;

    private static ServerStatus status = ServerStatus.Pending;

    public static ServerStatus Status
    {
        get => status;
        set
        {
            status = value;
        }
    }

    private static void Init()
    {
        disposed = false;

        clients.Clear();

        var port = ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort;

        if (port < 0 || port > 65535) port = null;

        listener = new(IPAddress.Any, port ?? 0);

        keepListen = true;
    }

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
                var client = listener.AcceptTcpClient();

                if (client.Client.RemoteEndPoint is not IPEndPoint endpoint) continue;

                clients.Add(endpoint.ToString(), client);

                Log.Information($"New plugin connection: {endpoint}");

                ReceiveMessage(client);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");

            Status = ServerStatus.Errored;
        }
    }

    /// <summary>
    /// 接收消息
    /// </summary>
    /// <param name="client">TcpClient</param>
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
                    var buffer = new byte[ConfigManager.AppConfig.Web.SocketBufferSize];

                    var length = stream is null ? 0 : await stream.ReadAsync(buffer);

                    if (length > 0)
                    {
                        onReceive?.Invoke(buffer, length, endpoint.ToString());

                        var msg = buffer.ToUTF8(0, length);

                        Log.Information($"From: {endpoint}\tReceive: {msg}");

                        if (msg.StartsWith("PluginStruct: "))
                        {
                            PluginsNetwork.Execute(msg[14..], endpoint);

                            var workPath = ConfigManager.AppConfig.App.LocalPluginsDataFolder.GetFullPath();
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

                Status = ServerStatus.Errored;
            }
            finally
            {
                if (endpoint is not null)
                {
                    PluginsNetwork.Disconnect(endpoint);

                    clients.Remove(endpoint.ToString());
                }

                stream?.CloseAndDispose();

                client.CloseAndDispose();
            }
        }).Start();
    }

    public async Task<PluginsServer> Broadcast(byte[] content)
    {
        await Task.Run(() =>
        {
            foreach (var client in clients.Values)
            {
                var stream = client.GetStream();

                stream.Write(content, 0, content.Length);

                stream.Flush();
            }
        });

        return this;
    }

    public async Task<PluginsServer> BroadCast(byte[] content, Func<TcpClient, bool>? pattern)
    {
        await Task.Run(() =>
        {
            foreach (var client in clients.Values)
                if (pattern?.Invoke(client) ?? false)
                {
                    var stream = client.GetStream();

                    stream.Write(content, 0, content.Length);

                    stream.Flush();
                }
        });

        return this;
    }

    public async Task<PluginsServer> Send(byte[] content, string target)
    {
        await Task.Run(() =>
        {
            var client = clients[target];

            var stream = client.GetStream();

            stream.Write(content, 0, content.Length);

            stream.Flush();
        });

        return this;
    }

    public PluginsServer OnReceive(Action<byte[], int?, string> action)
    {
        onReceive = action;

        return this;
    }

    public async Task<PluginsServer> Start()
    {
        var location = $"{nameof(PluginsServer)}.{nameof(Start)}";

        await TasksManager.RunTaskAsync(() =>
        {
            Status = ServerStatus.Starting;

            Init();

            if (listener is null) return;

            listener.Start();

            var port = ((IPEndPoint)listener.LocalEndpoint).Port; // 取服务端口号

            GlobalInfo.PluginServerPort = port; // 全局端口号标明

            EventService.Invoke(nameof(EventService.PluginsServerPortChanged));

            Log.Information($"PluginsServer Port: {port}");

            new Thread(AcceptClient).Start();

            Status = ServerStatus.Running;

        }, location);

        return this;
    }

    public async Task<PluginsServer> Stop()
    {
        var location = $"{nameof(PluginsServer)}.{nameof(Stop)}";

        Status = ServerStatus.Stopping;

        keepListen = false;

        await TasksManager.RunTaskAsync(() =>
        {
            foreach (KeyValuePair<string, TcpClient> item in clients)
            {
                item.Value.Close();
                item.Value.Dispose();
            }

            clients.Clear();

            listener?.Stop();

            Status = ServerStatus.Pending;

        }, location, catchException: true);

        return this;
    }

    public async Task<PluginsServer> Restart()
    {
        var location = $"{nameof(PluginsServer)}.{nameof(Restart)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            await Stop();

            await Start();

        }, location);

        return this;
    }

    public void Dispose()
    {
        if (disposed) return;

        disposed = true;

        listener = null;

        GC.SuppressFinalize(this);
    }
}
