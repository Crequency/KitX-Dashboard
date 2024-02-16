using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Names;
using KitX.Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;

namespace KitX.Dashboard.Managers;

public class ConfigManager
{
    internal class ConfigManagerInfo
    {
        private string? _location;

        internal string? Location
        {
            get => _location;
            set
            {
                ArgumentNullException.ThrowIfNull(value, nameof(Location));

                _location = Path.GetFullPath(value);

                if (!Directory.Exists(_location))
                    Directory.CreateDirectory(_location);
            }
        }
    }

    private readonly Dictionary<string, ConfigBase> _configs;

    internal ConfigManagerInfo? Infos;

    public ConfigManager()
    {
        Infos = new();

        _configs = [];

        InitEvents();
    }

    private void InitEvents()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(InitEvents)}";

        TasksManager.RunTask(() =>
        {

            EventService.AppConfigChanged += () => AppConfig.Save(AppConfig.ConfigFileLocation!);

            EventService.PluginsConfigChanged += () => PluginsConfig.Save(PluginsConfig.ConfigFileLocation!);

        }, location);
    }

    public ConfigManager SetLocation(string location)
    {
        if (Infos is not null)
            Infos.Location = location;

        return this;
    }

    private void RegisterFileWatcher<T>(T config) where T : ConfigBase, new()
    {
        var name = "ConfigFileWatcher".Append(typeof(T).Name);

        var path = config.ConfigFileLocation!;

        Instances.FileWatcherManager!.RegisterWatcher(
            name,
            path,
            (_, y) =>
            {
                var location = $"{nameof(ConfigManager)}.{nameof(RegisterFileWatcher)}";

                Log.Information($"FileChanged: {name} | {y.Name}, {y.ChangeType}");

                try
                {
                    _configs[typeof(T).Name] = path.Load<T>();
                }
                catch (Exception e)
                {
                    Log.Error(e, $"In {location}: {e.Message}");
                }
            }
        );
    }

    public ConfigManager LoadConfigFile<T>() where T : ConfigBase, new()
    {
        var name = typeof(T).Name;

        ArgumentNullException.ThrowIfNull(name, nameof(name));

        var path = $"{Infos?.Location}{name}.json".GetFullPath();

        var config = path.Load<T>().SetConfigFileLocation(path).Save(path);

        _configs.Add(name, config);

        if (ConstantTable.EnabledConfigFileHotReload)
            AppFramework.AfterInitailization(() =>
            {
                Instances.SignalTasksManager!.SignalRun(
                    nameof(SignalsNames.FileWatcherManagerInitializedSignal),
                    () => RegisterFileWatcher(config)
                );
            });

        return this;
    }

    public ConfigManager Load()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(Load)}";

        TasksManager.RunTask(() =>
        {
            LoadConfigFile<AppConfig>();
            LoadConfigFile<PluginsConfig>();
            LoadConfigFile<MarketConfig>();
        }, location, catchException: false);

        return this;
    }

    public ConfigManager SaveAll()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(SaveAll)}";

        TasksManager.RunTask(() =>
        {
            foreach (var config in _configs.Values)
                config.Save(config.ConfigFileLocation ?? throw new InvalidOperationException(
                    $"Saving config requires `{nameof(ConfigBase.ConfigFileLocation)}` property not null."
                ));

        }, location, catchException: true);

        return this;
    }

    private T GetConfig<T>(string name) where T : ConfigBase => _configs[name] as T ?? throw new Exception($"Can not find config: {name}");

    public AppConfig AppConfig => GetConfig<AppConfig>(nameof(AppConfig));

    public PluginsConfig PluginsConfig => GetConfig<PluginsConfig>(nameof(PluginsConfig));

    public MarketConfig MarketConfig => GetConfig<MarketConfig>(nameof(MarketConfig));
}
