using System;
using System.Threading.Tasks;

namespace KitX.Dashboard.Interfaces;

internal interface IModuleController<T> : IDisposable
{
    Task<T> Start();

    Task<T> Stop();

    Task<T> Restart();
}
