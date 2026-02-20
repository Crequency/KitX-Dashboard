using LiteDB;
using Common.BasicHelper.Core.TaskSystem;
using KitX.Core.Contract.FileWatcher;
using KitX.Core.Contract.Hotkey;
using KitX.Core.Contract.Security;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;

namespace KitX.Dashboard;

public static class Instances
{
    public static SignalTasksManager? SignalTasksManager { get; set; }

    /// <summary>
    /// Gets the plugin server from DI container
    /// </summary>
    public static KitX.Core.Device.PluginsServer? PluginsServer { get; set; }

    /// <summary>
    /// Gets the device discovery service from DI container
    /// </summary>
    public static KitX.Core.Device.DevicesDiscoveryServer? DevicesDiscoveryServer { get; set; }

    /// <summary>
    /// Gets the device server from DI container
    /// </summary>
    public static KitX.Core.Device.DevicesServer? DevicesServer { get; set; }

    public static LiteDatabase? ActivitiesDataBase { get; set; }

    /// <summary>
    /// Gets the Security service from DI container
    /// </summary>
    public static ISecurityService? SecurityService { get; set; }

    /// <summary>
    /// Gets the FileWatcher service from DI container
    /// </summary>
    public static IFileWatcherService? FileWatcherService { get; set; }

    /// <summary>
    /// Gets the KeyHook service from DI container
    /// </summary>
    public static IKeyHookService? KeyHookService { get; set; }

    /// <summary>
    /// Gets the KeyHookManager (alias for KeyHookService)
    /// </summary>
    public static KitX.Core.Hotkey.KeyHookManager? KeyHookManager => KeyHookService as KitX.Core.Hotkey.KeyHookManager;

    /// <summary>
    /// Gets the PluginsManager from DI container
    /// </summary>
    public static KitX.Core.Plugin.PluginsManager? PluginsManager { get; set; }

    internal static void Initialize()
    {
        const string location = $"{nameof(Instances)}.{nameof(Initialize)}";

        Log.Information("Instances.Initialize started...");

        // Get services from DI container
        var serviceProvider = App.GetService<IServiceProvider>();

        Log.Information("Got IServiceProvider, fetching other services...");

        SecurityService = serviceProvider.GetService<ISecurityService>();
        FileWatcherService = serviceProvider.GetService<IFileWatcherService>();
        KeyHookService = serviceProvider.GetService<IKeyHookService>();
        PluginsManager = serviceProvider.GetService<KitX.Core.Contract.Plugin.IPluginService>() as KitX.Core.Plugin.PluginsManager;

        Log.Information("Fetching network services...");

        // Get network services from DI container
        PluginsServer = serviceProvider.GetService<KitX.Core.Contract.Plugin.IPluginServer>() as KitX.Core.Device.PluginsServer;
        DevicesDiscoveryServer = serviceProvider.GetService<KitX.Core.Contract.Device.IDeviceDiscoveryService>() as KitX.Core.Device.DevicesDiscoveryServer;
        DevicesServer = serviceProvider.GetService<KitX.Core.Contract.Device.IDeviceServer>() as KitX.Core.Device.DevicesServer;

        Log.Information("Initializing SignalTasksManager...");

        // Initialize SignalTasksManager
        SignalTasksManager = new SignalTasksManager();

        Log.Information("Instances.Initialize completed.");
    }
}
