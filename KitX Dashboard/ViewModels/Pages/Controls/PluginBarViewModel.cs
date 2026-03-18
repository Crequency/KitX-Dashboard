using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Common.BasicHelper.Utils.Extensions;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.Device;
using KitX.Core.Event;
using KitX.Core.Plugin;
using KitX.Dashboard;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class PluginBarViewModel : ViewModelBase
{
    public PluginBarViewModel()
    {
        InitCommands();
        InitEvents();
    }

    public sealed override void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create(() =>
        {
            if (Plugin is not null && UIStateService.MainWindow is not null)
                new PluginDetailWindow() { WindowStartupLocation = WindowStartupLocation.CenterOwner }
                    .SetPluginInfo(Plugin.PluginInfo)
                    .Show(UIStateService.MainWindow);
        });

        RemoveCommand = ReactiveCommand.Create(() =>
        {
            if (Plugin is not null && PluginBar is not null)
            {
                PluginBars?.Remove(PluginBar);

                // Also remove from PluginsManager - use Id directly from installation
                var pluginService = App.GetService<KitX.Core.Contract.Plugin.IPluginService>();
                _ = pluginService.RemovePluginAsync(Plugin.Id);
            }
        });

        DeleteCommand = ReactiveCommand.Create(() =>
        {
            if (Plugin is not null && PluginBar is not null)
            {
                // First remove from PluginsManager (which also deletes files) - use Id directly
                var pluginService = App.GetService<KitX.Core.Contract.Plugin.IPluginService>();
                _ = pluginService.RemovePluginAsync(Plugin.Id);

                // Then remove from UI
                PluginBars?.Remove(PluginBar);
            }
        });

        LaunchCommand = ReactiveCommand.Create(() =>
        {
            const string location = $"{nameof(PluginBarViewModel)}.{nameof(LaunchCommand)}";

            if (Plugin?.LoaderInfo is null)
                return;

            new Thread(() =>
            {
                try
                {
                    var loaderName = Plugin?.LoaderInfo.LoaderName;
                    var loaderVersion = Plugin?.LoaderInfo.LoaderVersion;
                    var pd = Plugin?.PluginInfo;

                    // InstallPath is already an absolute path from PluginsManager
                    var pluginPath = $"{Plugin?.InstallPath}/{pd?.RootStartupFileName}";

                    var deviceService = App.GetService<IDeviceDiscoveryService>();

                    // Get actual port from PluginsServer instead of using ConstantTable
                    var pluginsServer = Instances.PluginsServer;
                    var actualPort = pluginsServer?.Port ?? 7777;  // Default to 7777 if not available

                    // Generate a unique connection ID (GUID) for this plugin instance
                    var connectionId = Guid.NewGuid().ToString();
                    // Use 127.0.0.1 instead of LAN IP since plugin and Dashboard run on the same machine
                    var connectStr =
                        "ws://127.0.0.1:"
                        + $"{actualPort}/"
                        + $"{connectionId}/";

                    if (Plugin is null)
                        return;

                    Log.Information($"Launch: {pluginPath}");

                    if (Plugin.LoaderInfo.SelfLoad)
                    {
                        Process.Start(pluginPath, $"--connect {connectStr}");
                    }
                    else
                    {
                        // Loader path - relative to app directory
                        var appDir = AppDomain.CurrentDomain.BaseDirectory;
                        var loaderPath = ConfigService.AppConfig.Loaders.InstallPath.TrimStart(new[] { '.', '/', '\\' });
                        var loaderFile = Path.Combine(appDir, loaderPath, loaderName ?? "", loaderVersion ?? "", loaderName ?? "");

                        if (OperatingSystem.IsWindows())
                            loaderFile += ".exe";

                        Log.Information($"Launch through loader: {loaderFile}");

                        // Get the actual plugin file - must use RootStartupFileName from PluginInfo
                        var pluginFile = pd?.RootStartupFileName;
                        if (string.IsNullOrEmpty(pluginFile))
                        {
                            Log.Error("RootStartupFileName is not specified in PluginInfo. Please ensure the plugin package includes this field.");
                            return;
                        }

                        // Build the full path to the plugin file
                        var pluginFilePath = Path.Combine(Plugin?.InstallPath ?? "", pluginFile);

                        if (!File.Exists(loaderFile))
                        {
                            Log.Error($"Loader not found: {loaderFile}. Please ensure the loader is installed.");
                            return;
                        }

                        if (!File.Exists(pluginFilePath))
                        {
                            Log.Error($"Plugin file not found: {pluginFilePath}. Please check RootStartupFileName in PluginInfo.json.");
                            return;
                        }

                        if (File.Exists(loaderFile) && File.Exists(pluginFilePath))
                        {
                            var arg = $"--load \"{pluginFilePath}\" --connect {connectStr}";

                            Log.Information($"Launch Argument: {arg}");

                            Process.Start(loaderFile, arg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }).Start();
        });
    }

    public sealed override void InitEvents()
    {
        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.LanguageChanged, (s, e) => this.RaisePropertyChanged(nameof(DisplayName)));
    }

    internal PluginBar? PluginBar { get; set; }

    internal PluginInstallation? Plugin { get; set; }

    internal string? DisplayName
    {
        get
        {
            if (Plugin is null)
                return null;

            return Plugin.PluginInfo.DisplayName.TryGetValue(ConfigService.AppConfig.App.AppLanguage, out var lang)
                ? lang
                : Plugin.PluginInfo.DisplayName.Values.GetEnumerator().Current;
        }
    }

    internal string? AuthorName => Plugin?.PluginInfo.AuthorName;

    internal string? Version => Plugin?.PluginInfo.Version;

    internal ObservableCollection<PluginBar>? PluginBars { get; set; }

    internal Bitmap IconDisplay
    {
        get
        {
            const string location = $"{nameof(PluginBarViewModel)}.{nameof(IconDisplay)}.getter";

            try
            {
                if (Plugin is null)
                    return App.DefaultIcon;

                var src = Convert.FromBase64String(Plugin.PluginInfo.IconInBase64);

                using var ms = new MemoryStream(src);

                return new(ms);
            }
            catch (Exception e)
            {
                Log.Warning(
                    e,
                    $"In {location}: "
                        + $"Failed to transform icon from base64 to byte[] "
                        + $"or create bitmap from `MemoryStream`. {e.Message}"
                );

                return App.DefaultIcon;
            }
        }
    }

    internal ReactiveCommand<Unit, Unit>? ViewDetailsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RemoveCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? DeleteCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? LaunchCommand { get; set; }
}
