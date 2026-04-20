using LiteDB;
using Common.BasicHelper.Core.TaskSystem;
using KitX.Core.Contract.FileWatcher;
using KitX.Core.Contract.Hotkey;
using KitX.Core.Contract.Security;
using KitX.Core.DI;
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
    public static IDeviceKeyService? SecurityService { get; set; }

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
        Log.Information("Instances.Initialize started...");

        // Get services from the single ServiceHost provider
        var provider = ServiceHost.ServiceProvider;

        Log.Information("Got IServiceProvider, fetching other services...");

        SecurityService = provider.GetService<IDeviceKeyService>();
        FileWatcherService = provider.GetService<IFileWatcherService>();
        KeyHookService = provider.GetService<IKeyHookService>();
        PluginsManager = provider.GetService<KitX.Core.Contract.Plugin.IPluginService>() as KitX.Core.Plugin.PluginsManager;

        Log.Information("Fetching network services...");

        // All services now come from the same ServiceProvider via ServiceHost,
        // so PluginsServer.Instance and provider.GetService<IPluginServer>() return the same object
        PluginsServer = KitX.Core.Device.PluginsServer.Instance as KitX.Core.Device.PluginsServer;
        Log.Information("[Instances] PluginsServer from Instance: HashCode={HashCode}", PluginsServer?.GetHashCode());
        DevicesDiscoveryServer = provider.GetService<KitX.Core.Contract.Device.IDeviceDiscoveryService>() as KitX.Core.Device.DevicesDiscoveryServer;
        DevicesServer = provider.GetService<KitX.Core.Contract.Device.IDeviceServer>() as KitX.Core.Device.DevicesServer;

        Log.Information("Initializing SignalTasksManager...");

        // Initialize SignalTasksManager
        SignalTasksManager = new SignalTasksManager();

        // Resolve TriggerManager to activate event subscriptions
        var triggerManager = provider.GetService<KitX.Core.Workflow.TriggerManager>();
        Log.Information("TriggerManager resolved: {Resolved}", triggerManager != null);

        Log.Information("Instances.Initialize completed.");
    }
}

