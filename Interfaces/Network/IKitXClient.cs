using System;
using System.Threading.Tasks;

namespace KitX_Dashboard.Interfaces.Network;

internal interface IKitXClient<T> : IModuleController<T>
{
    Task<T> Connect();

    Task<T> Disconnect();

    Task<T> Send(byte[] content);

    Task<T> OnReceive(Action<byte[]> action);
}
