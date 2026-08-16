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
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Device;
using KitX.Core.Plugin;
using KitX.Dashboard;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class PluginBarViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IEventService _eventService;
    private readonly IPluginService _pluginService;
    private readonly IPluginServer _pluginServer;

    /// <summary>Named handler so <see cref="Dispose"/> can unsubscribe it (D11).</summary>
    private readonly EventHandler<EventArgs> _languageChangedHandler;

    public PluginBarViewModel(
        IConfigService configService,
        IEventService eventService,
        IPluginService pluginService,
        IPluginServer pluginServer)
    {
        _configService = configService;
        _eventService = eventService;
        _pluginService = pluginService;
        _pluginServer = pluginServer;

        _languageChangedHandler = (_, _) => this.RaisePropertyChanged(nameof(DisplayName));

        InitCommands();
        InitEvents();
    }

    public sealed override void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create(() =>
        {
            if (Plugin is not null && UIStateService.MainWindow is not null)
                new PluginDetailWindow() { WindowStartupLocation = WindowStartupLocation.CenterOwner }
                    .SetPluginInfo(Plugin.PluginInfo!)
                    .Show(UIStateService.MainWindow);
        });

        // D6: Remove/Delete differ only in the order of UI removal vs. file deletion.
        // removeFirst = true keeps the card until deletion succeeds (Delete), false
        // removes it immediately (Remove).
        RemoveCommand = ReactiveCommand.Create(() => UninstallPlugin(removeFirst: false));

        DeleteCommand = ReactiveCommand.Create(() => UninstallPlugin(removeFirst: true));

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

                    // Get actual port from the contract (no cast to the concrete type)
                    var actualPort = _pluginServer.Port;
                    if (actualPort is null or 0)
                    {
                        Log.Error("Cannot launch plugin: PluginsServer is not running");
                        return;
                    }

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
                        var loaderPath = _configService.AppConfig.Loaders.InstallPath.TrimStart(new[] { '.', '/', '\\' });
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
        _eventService.Subscribe(EventNames.LanguageChanged, _languageChangedHandler);
    }

    /// <summary>
    /// Unsubscribes the language handler. Called when the owning <see cref="PluginBar"/>
    /// leaves the visual tree — a transient VM per card would otherwise accumulate one
    /// subscription per refresh (RepoPage rebuilds the bar list on every refresh).
    /// </summary>
    public void Dispose()
    {
        _eventService.Unsubscribe(EventNames.LanguageChanged, _languageChangedHandler);
    }

    internal PluginBar? PluginBar { get; set; }

    internal PluginInstallation? Plugin { get; set; }

    /// <summary>
    /// Shared uninstall path for the Remove / Delete commands (D6).
    /// <paramref name="removeFirst"/> controls whether the card is removed from the UI
    /// before the plugin is deleted from disk (Remove) or afterwards (Delete).
    /// </summary>
    private void UninstallPlugin(bool removeFirst)
    {
        if (Plugin is null || PluginBar is null)
            return;

        if (removeFirst)
            PluginBars?.Remove(PluginBar);

        _ = _pluginService.RemovePluginAsync(Plugin.Id);

        if (!removeFirst)
            PluginBars?.Remove(PluginBar);
    }

    internal string? DisplayName
    {
        get
        {
            if (Plugin is null)
                return null;

            return Plugin.PluginInfo!.DisplayName.TryGetValue(_configService.AppConfig.App.AppLanguage, out var lang)
                ? lang
                : Plugin.PluginInfo.DisplayName.Values.GetEnumerator().Current;
        }
    }

    internal string? AuthorName => Plugin?.PluginInfo?.AuthorName;

    internal string? Version => Plugin?.PluginInfo?.Version;

    internal ObservableCollection<PluginBar>? PluginBars { get; set; }

    /// <summary>
    /// Cached decoded icons, keyed by base64 (D4). Bitmaps are owned by this cache;
    /// they are replaced and disposed when the icon source changes, so a plugin card
    /// never decodes its icon more than once. A small cache (one entry per rendered
    /// plugin card) is bounded by the plugin count.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Bitmap> _iconCache = new();

    private string? _iconSourceKey;

    private Bitmap? _cachedIcon;

    internal Bitmap IconDisplay
    {
        get
        {
            const string location = $"{nameof(PluginBarViewModel)}.{nameof(IconDisplay)}.getter";

            try
            {
                if (Plugin is null)
                    return App.DefaultIcon!;

                var src = Plugin.PluginInfo!.IconInBase64;

                // Same base64 → reuse the cached bitmap (only one decode per icon).
                if (_iconSourceKey == src && _cachedIcon is not null)
                    return _cachedIcon;

                if (_iconCache.TryGetValue(src, out var cached))
                {
                    _cachedIcon = cached;
                    _iconSourceKey = src;
                    return cached;
                }

                var bytes = Convert.FromBase64String(src);

                using var ms = new MemoryStream(bytes);

                var bitmap = new Bitmap(ms);

                _iconCache[src] = bitmap;
                _cachedIcon = bitmap;
                _iconSourceKey = src;

                return bitmap;
            }
            catch (Exception e)
            {
                Log.Warning(
                    e,
                    $"In {location}: "
                        + $"Failed to transform icon from base64 to byte[] "
                        + $"or create bitmap from `MemoryStream`. {e.Message}"
                );

                return App.DefaultIcon!;
            }
        }
    }

    internal ReactiveCommand<Unit, Unit>? ViewDetailsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RemoveCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? DeleteCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? LaunchCommand { get; set; }
}
