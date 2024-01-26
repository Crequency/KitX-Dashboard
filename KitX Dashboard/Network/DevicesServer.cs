using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Data;
using KitX.Dashboard.Interfaces.Network;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KitX.Dashboard.Network;

internal class DevicesServer : IKitXServer<DevicesServer>
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

            if (status == ServerStatus.Errored)
                DevicesNetwork.Restart();
        }
    }

    private static void Init()
    {
        disposed = false;

        clients.Clear();

        listener = new(IPAddress.Any, 0);

        keepListen = true;
    }

    private static void AcceptClient()
    {
        var location = $"{nameof(DevicesServer)}.{nameof(AcceptClient)}";

        try
        {
            while (keepListen && listener is not null)
            {
                var client = listener.AcceptTcpClient();

                if (client.Client.RemoteEndPoint is not IPEndPoint endpoint) continue;

                clients.Add(endpoint.ToString(), client);

                Log.Information($"New device connection: {endpoint}");

                ReceiveMessage(client);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {nameof(location)}: {ex.Message}");

            Status = ServerStatus.Errored;
        }
    }

    private static void ReceiveMessage(TcpClient client)
    {
        var location = $"{nameof(DevicesServer)}.{nameof(ReceiveMessage)}";

        IPEndPoint? endpoint = null;
        NetworkStream? stream = null;

        new Thread(async () =>
        {
            try
            {
                endpoint = client?.Client.RemoteEndPoint as IPEndPoint;

                stream = client?.GetStream();

                if (endpoint is null || stream is null) return;

                while (keepListen)
                {
                    var buffer = new byte[ConfigManager.AppConfig.Web.SocketBufferSize];

                    var length = await stream.ReadAsync(buffer);

                    if (length > 0)
                    {
                        onReceive?.Invoke(buffer, length, endpoint.ToString());

                        var msg = buffer.ToUTF8(0, length);

                        Log.Information($"From: {endpoint}\tReceive: {msg}");
                    }
                    else break; // 客户端断开连接 跳出循环
                }
            }
            catch (Exception ex)
            {
                Log.Error($"In {location}: {ex.Message}");
                Log.Information($"Connection broke from: {endpoint}");
            }
            finally
            {
                if (endpoint is not null)
                {
                    clients.Remove(endpoint.ToString());
                }

                stream?.CloseAndDispose();

                client?.CloseAndDispose();
            }
        }).Start();
    }

    public async Task<DevicesServer> Broadcast(byte[] content)
    {
        var location = $"{nameof(DevicesServer)}.{nameof(Broadcast)}";

        await TasksManager.RunTaskAsync(() =>
        {
            foreach (var client in clients)
                client.Value.Client.Send(content);
        }, location, catchException: true);

        return this;
    }

    public async Task<DevicesServer> BroadCast(byte[] content, Func<TcpClient, bool>? pattern)
    {
        var location = $"{nameof(DevicesServer)}.{nameof(BroadCast)}";

        await TasksManager.RunTaskAsync(() =>
        {
            foreach (var client in clients)
            {
                if (pattern is not null && pattern.Invoke(client.Value))
                    client.Value.Client.Send(content);
                else client.Value.Client.Send(content);
            }
        }, location, catchException: true);

        return this;
    }

    public async Task<DevicesServer> Send(byte[] content, string target)
    {
        var location = $"{nameof(DevicesServer)}.{nameof(Send)}";

        await TasksManager.RunTaskAsync(() =>
        {
            if (clients.TryGetValue(target, out var client))
                client.Client.Send(content);
        }, location, catchException: true);

        return this;
    }

    public DevicesServer OnReceive(Action<byte[], int?, string> action)
    {
        onReceive = action;

        return this;
    }

    public async Task<DevicesServer> Start()
    {
        var location = $"{nameof(DevicesServer)}.{nameof(Start)}";

        if (Status != ServerStatus.Pending) return this;

        await TasksManager.RunTaskAsync(() =>
        {
            Status = ServerStatus.Starting;

            Init();

            if (listener is null) return;

            listener.Start();

            var port = ((IPEndPoint)listener.LocalEndpoint).Port; // 取服务端口号

            GlobalInfo.DevicesServerPort = port; // 全局端口号标明
            GlobalInfo.ServerBuildTime = DateTime.UtcNow;
            GlobalInfo.IsMainMachine = true;

            Log.Information($"DevicesServer Port: {port}");

            EventService.Invoke(nameof(EventService.DevicesServerPortChanged));

            new Thread(AcceptClient).Start();

            Status = ServerStatus.Running;

        }, location, catchException: true);

        return this;
    }

    public async Task<DevicesServer> Stop()
    {
        var location = $"{nameof(DevicesServer)}.{nameof(Stop)}";

        if (Status != ServerStatus.Running) return this;

        await TasksManager.RunTaskAsync(() =>
        {
            Status = ServerStatus.Stopping;

            listener?.Stop();

            keepListen = false;

            foreach (KeyValuePair<string, TcpClient> item in clients)
            {
                item.Value.Close();
                item.Value.Dispose();
            }

            clients.Clear();

            GlobalInfo.IsMainMachine = false;
            GlobalInfo.DevicesServerPort = -1;

            EventService.Invoke(nameof(EventService.DevicesServerPortChanged));

            Status = ServerStatus.Pending;

        }, location, catchException: true);

        return this;
    }

    public async Task<DevicesServer> Restart()
    {
        var location = $"{nameof(DevicesServer)}.{nameof(Restart)}";

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

        GC.SuppressFinalize(this);
    }
}
