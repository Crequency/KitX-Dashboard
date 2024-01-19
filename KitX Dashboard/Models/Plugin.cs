using KitX.Web.Rules;
using KitX.Web.Rules.Plugin;
using KitX.Web.Rules.Device;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitX.Dashboard.Models;

public class Plugin
{
    /// <summary>
    /// 插件安装路径
    /// </summary>
    [JsonInclude]
    public string? InstallPath { get; set; }

    /// <summary>
    /// 该插件的详细信息
    /// </summary>
    [JsonIgnore]
    public PluginInfo PluginDetails { get; set; }

    /// <summary>
    /// 需要的加载器的详细信息
    /// </summary>
    [JsonIgnore]
    public LoaderInfo RequiredLoaderInfo { get; set; }

    /// <summary>
    /// 已安装此插件的网络设备
    /// </summary>
    [JsonIgnore]
    public List<DeviceLocator>? InstalledDevices { get; set; }
}
