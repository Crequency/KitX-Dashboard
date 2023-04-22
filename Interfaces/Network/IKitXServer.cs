using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace KitX_Dashboard.Interfaces.Network;

internal interface IKitXServer<T> : IModuleController<T>
{
    Task<T> Broadcast(byte[] content);

    Task<T> BroadCast(byte[] content, Func<TcpClient, bool>? pattern);

    Task<T> Send(byte[] content, string target);

    Task<T> OnReceive(Action<byte[], string> action);
}
