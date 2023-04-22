using System;
using System.Threading.Tasks;

namespace KitX_Dashboard.Interfaces.Network;

internal interface IKitXClient<T> : IModuleController<T>
{
    Task<T> Connect();

    Task<T> Disconnect();

    Task<T> Send(byte[] content);

    T OnReceive(Action<byte[], int?> action);
}
