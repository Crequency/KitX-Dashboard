using Common.BasicHelper.IO;
using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using KitX_Dashboard.Names;
using KitX_Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KitX_Dashboard.Managers;

internal class ConfigManager
{
    private static readonly object _appConfigWriteLock = new();

    private static readonly object _pluginsListConfigWriteLock = new();

    internal static AppConfig AppConfig = new();

    internal static void Init()
    {
        InitConfigs();

        InitEvents();

        InitFileWatcher();
    }

    internal static void InitConfigs()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(InitConfigs)}";

        TasksManager.RunTask(() =>
        {
            try
            {
                var configDir = GlobalInfo.ConfigPath.GetFullPath();
                var configFilePath = GlobalInfo.ConfigFilePath.GetFullPath();
                var pluginsListConfigFilePath = GlobalInfo.PluginsListConfigFilePath.GetFullPath();

                if (!Directory.Exists(configDir))
                    _ = Directory.CreateDirectory(configDir);

                if (!File.Exists(configFilePath))
                    SaveAppConfig();
                else
                    LoadAppConfig();

                if (!File.Exists(pluginsListConfigFilePath))
                    SavePluginsListConfig();
                else
                    LoadPluginsListConfig();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        }, location);
    }

    internal static void InitEvents()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(InitEvents)}";

        TasksManager.RunTask(() =>
        {
            EventService.ConfigSettingsChanged += () => SaveAppConfig();

            EventService.PluginsListChanged += () => SavePluginsListConfig();

        }, location);
    }

    internal static void InitFileWatcher()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(InitFileWatcher)}";

        var appConfigWatcher = nameof(FileWatcherNames.AppConfigFileWatcher);

        Program.FileWatcherManager?.RegisterWatcher(
            appConfigWatcher,
            GlobalInfo.ConfigFilePath.GetFullPath(),
            (x, y) =>
            {
                Log.Information($"{appConfigWatcher}: {y.Name}, {y.ChangeType}");

                try
                {
                    lock (_appConfigWriteLock)
                    {
                        AppConfig = JsonSerializer.Deserialize<AppConfig>(
                            File.ReadAllText(GlobalInfo.ConfigFilePath)
                        ) ?? new();

                        EventService.Invoke(nameof(EventService.OnConfigHotReloaded));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }
        );
    }

    internal static void SaveConfigs()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(SaveConfigs)}";

        TasksManager.RunTask(() =>
        {
            try
            {
                SaveAppConfig();
                SavePluginsListConfig();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        }, location);
    }

    internal static async void LoadAppConfig()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(LoadAppConfig)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            try
            {
                AppConfig = JsonSerializer.Deserialize<AppConfig>(
                    await FileHelper.ReadAllAsync(GlobalInfo.ConfigFilePath)
                ) ?? new();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");

                AppConfig = new AppConfig();
            }
        }, location);
    }

    internal static void SaveAppConfig()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(SaveAppConfig)}";

        var options = new JsonSerializerOptions()
        {
            WriteIndented = true,
            IncludeFields = true,
        };

        TasksManager.RunTask(() =>
        {
            try
            {
                lock (_appConfigWriteLock)
                {
                    File.WriteAllText(
                        GlobalInfo.ConfigFilePath.GetFullPath(),
                        JsonSerializer.Serialize(ConfigManager.AppConfig, options)
                    );
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"In {location}: {ex.Message}");
            }
        }, location);
    }

    internal static async void LoadPluginsListConfig()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(LoadPluginsListConfig)}";

        await TasksManager.RunTaskAsync(async () =>
        {
            try
            {
                PluginsManager.Plugins = JsonSerializer.Deserialize<List<Plugin>>(
                    await FileHelper.ReadAllAsync(GlobalInfo.PluginsListConfigFilePath)
                ) ?? new();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
                PluginsManager.Plugins = new();
            }
        }, location);
    }

    internal static void SavePluginsListConfig()
    {
        var location = $"{nameof(ConfigManager)}.{nameof(SavePluginsListConfig)}";

        var options = new JsonSerializerOptions()
        {
            WriteIndented = true,
            IncludeFields = true,
        };

        TasksManager.RunTask(() =>
        {
            lock (_pluginsListConfigWriteLock)
            {
                File.WriteAllText(
                    GlobalInfo.PluginsListConfigFilePath.GetFullPath(),
                    JsonSerializer.Serialize(PluginsManager.Plugins, options)
                );
            }
        }, location);
    }
}
