using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;
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
