using KitX_Dashboard.Interfaces.Network;
using System;
using System.Threading.Tasks;

namespace KitX_Dashboard.Network;

internal class DevicesClient : IKitXClient<DevicesClient>
{
    public async Task<DevicesClient> Connect()
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesClient> Disconnect()
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesClient> OnReceive(Action<byte[]> action)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesClient> Send(byte[] content)
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesClient> Start()
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesClient> Stop()
    {
        await Task.Run(() => { });

        return this;
    }

    public async Task<DevicesClient> Restart()
    {
        await Task.Run(async () =>
        {
            await Start();

            await Stop();
        });

        return this;
    }

    public void Dispose()
    {

    }
}
