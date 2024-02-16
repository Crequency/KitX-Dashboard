using Common.BasicHelper.Utils.Extensions;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KitX.Dashboard.Configuration;

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
[JsonDerivedType(typeof(ConfigBase), typeDiscriminator: nameof(ConfigBase))]
[JsonDerivedType(typeof(AppConfig), typeDiscriminator: nameof(AppConfig))]
[JsonDerivedType(typeof(PluginsConfig), typeDiscriminator: nameof(PluginsConfig))]
[JsonDerivedType(typeof(MarketConfig), typeDiscriminator: nameof(MarketConfig))]
public class ConfigBase
{
    public string? ConfigFileLocation { get; set; }

    public DateTime? ConfigGeneratedTime { get; set; } = DateTime.Now;
}

public static class ConfigBaseExtensions
{
    private static readonly JsonSerializerOptions serializationOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    public static T Load<T>(this string path) where T : ConfigBase, new()
    {
        path = path.GetFullPath();

        if (!File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path);

            if (!Directory.Exists(dir) && dir is not null)
                Directory.CreateDirectory(dir);

            var conf = new T().Save(path);

            return conf;
        }

        var text = File.ReadAllText(path);

        var result = JsonSerializer.Deserialize<ConfigBase>(text, serializationOptions);

        return result as T ?? throw new Exception("Can not deserialize config file.");
    }

    public static T Save<T>(this T config, string path) where T : ConfigBase
    {
        path = path.GetFullPath();

        var text = JsonSerializer.Serialize<ConfigBase>(config, serializationOptions);

        File.WriteAllText(path, text);

        return config;
    }

    public static T SetConfigFileLocation<T>(this T config, string path) where T : ConfigBase
    {
        config.ConfigFileLocation = path;

        return config;
    }
}
