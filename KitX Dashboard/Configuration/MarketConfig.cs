using System.Collections.Generic;

namespace KitX.Dashboard.Configuration;

public class MarketConfig : ConfigBase
{
    public Dictionary<string, string> Sources { get; set; } = new()
    {
        { "KitX Official Market Source", "https://cget.catrol.cn/KitX/v1/index.json" }
    };
}
