using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Common.BasicHelper.Utils.Extensions;

namespace KitX.Dashboard.Configuration;

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
[JsonDerivedType(typeof(ConfigBase), typeDiscriminator: nameof(ConfigBase))]
[JsonDerivedType(typeof(AppConfig), typeDiscriminator: nameof(AppConfig))]
[JsonDerivedType(typeof(PluginsConfig), typeDiscriminator: nameof(PluginsConfig))]
[JsonDerivedType(typeof(MarketConfig), typeDiscriminator: nameof(MarketConfig))]
[JsonDerivedType(typeof(AnnouncementConfig), typeDiscriminator: nameof(AnnouncementConfig))]
[JsonDerivedType(typeof(SecurityConfig), typeDiscriminator: nameof(SecurityConfig))]
public class ConfigBase
{
    public string? ConfigFileLocation { get; set; }

    public string? ConfigFileWatcherName { get; set; }

    public DateTime? ConfigGeneratedTime { get; set; } = DateTime.Now;
}

public static class ConfigBaseExtensions
{
    private static readonly object _configReadWriteLock = new();

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

        string text;

        lock (_configReadWriteLock)
        {
            text = File.ReadAllText(path);
        }

        var result = JsonSerializer.Deserialize<ConfigBase>(text, serializationOptions);

        return result as T ?? throw new Exception("Can not deserialize config file.");
    }

    public static T Save<T>(this T config, string path) where T : ConfigBase
    {
        path = path.GetFullPath();

        lock (_configReadWriteLock)
        {
            var text = JsonSerializer.Serialize<ConfigBase>(config, serializationOptions);

            File.WriteAllText(path, text);
        }

        return config;
    }

    public static T SetConfigFileLocation<T>(this T config, string path) where T : ConfigBase
    {
        config.ConfigFileLocation = path;

        return config;
    }
}
