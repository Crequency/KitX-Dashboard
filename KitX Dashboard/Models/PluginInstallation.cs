using KitX.Shared.Device;
using KitX.Shared.Loader;
using KitX.Shared.Plugin;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitX.Dashboard.Models;

public class PluginInstallation
{
    [JsonInclude]
    public string? InstallPath { get; set; }

    [JsonIgnore]
    public PluginInfo PluginInfo { get; set; } = new();

    [JsonIgnore]
    public LoaderInfo LoaderInfo { get; set; } = new();

    [JsonIgnore]
    public List<DeviceLocator> InstalledDevices { get; set; } = [];
}
