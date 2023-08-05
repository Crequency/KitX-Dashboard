using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitX.Dashboard.Data;

/// <summary>
/// 市场配置文件
/// </summary>
public class MarketConfig
{
    [JsonInclude]
    public Dictionary<string, string> Sources { get; set; } = new()
    {
        { "KitX Official Market Source", "https://cget.catrol.cn/KitX/v1/index.json" }
    };
}
