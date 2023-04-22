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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.Network;

internal class PluginsServer : IKitXServer<PluginsServer>
{
    private static TcpListener? listener = null;

    private static bool keepListen = true;

    private static readonly Dictionary<string, TcpClient> clients = new();

    private static Action<byte[], int?, string>? onReceive = null;

    /// <summary>
    /// 接收客户端
    /// </summary>
    private static void AcceptClient()
    {
        var location = $"{nameof(PluginsServer)}.{nameof(AcceptClient)}";

        try
        {
            while (keepListen)
            {
                if (listener.Pending())
                {
                    var client = listener.AcceptTcpClient();
                    var endpoint = client.Client.RemoteEndPoint as IPEndPoint;

                    clients.Add(endpoint.ToString(), client);

                    Log.Information($"New plugin connection: {endpoint}");

                    ReciveMessage(client);
                }
                //else
                //{
                //    Thread.Sleep(100);
                //}
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
    private static void ReciveMessage(TcpClient client)
    {
        var location = $"{nameof(PluginsServer)}.{nameof(ReciveMessage)}";

        IPEndPoint? endpoint = null;
        NetworkStream? stream = null;

        new Thread(async () =>
        {
            try
            {
                endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                stream = client.GetStream();

                while (keepListen)
                {
                    var buffer = new byte[Program.Config.Web.SocketBufferSize];

                    var length = await stream.ReadAsync(buffer);

                    if (length > 0)
                    {
                        onReceive?.Invoke(buffer, length, endpoint.ToString());

                        var msg = Encoding.UTF8.GetString(buffer, 0, length);

                        Log.Information($"From: {endpoint}\tReceive: {msg}");

                        if (msg.StartsWith("PluginStruct: "))
                        {
                            PluginsManager.Execute(msg[14..], endpoint);

                            var workPath = Program.Config.App.LocalPluginsDataFolder.GetFullPath();
                            var sendtxt = $"WorkPath: {workPath}";
                            var bytes = Encoding.UTF8.GetBytes(sendtxt);

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
                PluginsManager.Disconnect(endpoint);

                stream?.CloseAndDispose();

                clients.Remove(endpoint.ToString());

                client.Dispose();
            }
        }).Start();
    }

    private static void Init()
    {
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
        listener.Stop();

        GC.SuppressFinalize(this);
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。
