using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Interfaces.Network;
using KitX_Dashboard.Managers;
using Serilog;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KitX_Dashboard.Network;

internal class DevicesClient : IKitXClient<DevicesClient>
{
    private static TcpClient? client = null;

    private static bool keepReceiving = false;

    private static ClientStatus status = ClientStatus.Unknown;

    public static string ServerAddress { get; set; } = string.Empty;

    public static int ServerPort { get; set; } = 0;

    public static ClientStatus Status
    {
        get => status;
        set
        {
            status = value;

            if (status == ClientStatus.Errored)
                DevicesNetwork.Restart();
        }
    }

    public static Action<byte[], int?>? onReceive = null;

    public DevicesClient SetServerAddress(string address)
    {
        ServerAddress = address;

        return this;
    }

    public DevicesClient SetServerPort(int port)
    {
        if (port <= 0 || port >= 65536)
            throw new ArgumentOutOfRangeException(nameof(port), "0 < port < 65536");

        ServerPort = port;

        return this;
    }

    public async void Receive()
    {
        var location = $"{nameof(DevicesClient)}.{nameof(Receive)}";

        if (client is null) return;

        var stream = client?.GetStream();

        if (stream is null) return;

        var buffer = new byte[Program.Config.Web.SocketBufferSize];  //  Default 10 MB buffer

        try
        {
            while (keepReceiving)
            {
                if (buffer is null) break;

                var length = await stream.ReadAsync(buffer);

                if (length > 0)
                {
                    var msg = buffer.ToUTF8(0, length);

                    Log.Information($"Receive from Host: {msg}");
                }
                else
                {
                    keepReceiving = false;
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, $"In {location}: {e.Message}");

            Status = ClientStatus.Errored;
        }

        stream.CloseAndDispose();

        client?.CloseAndDispose();
    }

    public async Task<DevicesClient> Connect()
    {
        var location = $"{nameof(DevicesClient)}.{nameof(Connect)}";

        if (client is null) return this;

        Status = ClientStatus.Connecting;

        await TasksManager.RunTaskAsync(async () =>
        {
            try
            {
                await client.ConnectAsync(ServerAddress, ServerPort);

                new Thread(Receive).Start();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");

                await Stop();

                Status = ClientStatus.Errored;
            }

        }, location);

        Status = ClientStatus.Running;

        return this;
    }

    public async Task<DevicesClient> Disconnect()
    {
        var location = $"{nameof(DevicesClient)}.{nameof(Disconnect)}";

        Status = ClientStatus.Disconnecting;

        await TasksManager.RunTaskAsync(() =>
        {
            keepReceiving = false;

            client?.Close();

        }, location);

        Status = ClientStatus.Pending;

        return this;
    }

    public DevicesClient OnReceive(Action<byte[], int?> action)
    {
        onReceive = action;

        return this;
    }

    public async Task<DevicesClient> Send(byte[] content)
    {
        var location = $"{nameof(DevicesClient)}.{nameof(Send)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            try
            {
                var stream = client?.GetStream();

                if (stream is null) return;

                stream.Write(content, 0, content.Length);
                stream.Flush();

                Log.Information($"Sent Message to Host, msg: {content.ToUTF8()}");
            }
            catch (Exception e)
            {
                Log.Error(e, $"In {location}: {e.Message}");

                await Stop();

                Status = ClientStatus.Errored;
            }
        }, location);

        return this;
    }

    private static void Init()
    {
        keepReceiving = true;

        client = new();
    }

    public async Task<DevicesClient> Start()
    {
        var location = $"{nameof(DevicesClient)}.{nameof(Start)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            Status = ClientStatus.Pending;

            Init();

            await Connect();

        }, location);

        return this;
    }

    public async Task<DevicesClient> Stop()
    {
        var location = $"{nameof(DevicesClient)}.{nameof(Stop)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            keepReceiving = false;

            await Disconnect();

        }, location);

        return this;
    }

    public async Task<DevicesClient> Restart()
    {
        var location = $"{nameof(DevicesClient)}.{nameof(Restart)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            await Stop();

            await Start();

        }, location);

        return this;
    }

    public void Dispose()
    {
        client?.Dispose();
    }
}
