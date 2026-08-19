using System.Collections.ObjectModel;
using KitX.Core.Contract.Device;
using KitX.Shared.CSharp.Plugin;
using KitX.ToolKit.Models;

namespace KitX.Dashboard.Services;

/// <summary>
/// Default <see cref="IGlobalDataStore"/> implementation: owns the three shared observable
/// collections. Public parameterless constructor so DI (and headless tests) can create it
/// directly without reflection surprises.
/// </summary>
public class GlobalDataStore : IGlobalDataStore
{
    public ObservableCollection<IDeviceCase> DeviceCases { get; } = [];

    public ObservableCollection<PluginInfo> PluginInfos { get; } = [];

    public ObservableCollection<Toolkit> Toolkits { get; } = [];
}
