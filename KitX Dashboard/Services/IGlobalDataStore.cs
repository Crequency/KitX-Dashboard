using System.Collections.ObjectModel;
using KitX.Core.Contract.Device;
using KitX.Shared.CSharp.Plugin;
using KitX.ToolKit.Models;

namespace KitX.Dashboard.Services;

/// <summary>
/// Shared, cross-window observable data collections formerly owned by the retired static
/// UI-state service. Registered as a DI singleton so ViewModels that subscribe to
/// plugin/device state (e.g. <see cref="ViewModels.AppViewModel"/> tray text) and pages
/// that render those collections all read the same instances.
/// </summary>
public interface IGlobalDataStore
{
    /// <summary>Discovered device cases, shared across the dashboard pages.</summary>
    ObservableCollection<IDeviceCase> DeviceCases { get; }

    /// <summary>Connected plugin infos, shared across pages / the plugin launcher.</summary>
    ObservableCollection<PluginInfo> PluginInfos { get; }

    /// <summary>
    /// ToolKits loaded for the ToolKit management page. Retained for parity with the
    /// retired static Toolkits member.
    /// </summary>
    ObservableCollection<Toolkit> Toolkits { get; }
}
