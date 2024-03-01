using System.Collections.Generic;
using KitX.Dashboard.Models;

namespace KitX.Dashboard.Configuration;

public class PluginsConfig : ConfigBase
{
    public List<PluginInstallation> Plugins { get; set; } = [];
}
