using KitX.Dashboard.Models;
using System.Collections.Generic;

namespace KitX.Dashboard.Configuration;

public class PluginsConfig : ConfigBase
{
    public List<Plugin> Plugins { get; set; } = [];
}
