using KitX.Web.Rules;
using KitX.Web.Rules.Plugin;
using KitX.Web.Rules.Device;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitX.Dashboard.Models;

public class Plugin
{



    [JsonInclude]
    public string? InstallPath { get; set; }




    [JsonIgnore]
    public PluginInfo PluginDetails { get; set; }




    [JsonIgnore]
    public LoaderInfo RequiredLoaderInfo { get; set; }




    [JsonIgnore]
    public List<DeviceLocator>? InstalledDevices { get; set; }
}
