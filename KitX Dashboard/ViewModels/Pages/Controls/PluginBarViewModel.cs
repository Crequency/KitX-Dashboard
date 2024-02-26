using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Models;
using KitX.Dashboard.Network.DevicesNetwork;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Threading;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class PluginBarViewModel : ViewModelBase
{
    public PluginBarViewModel()
    {
        InitCommands();
        InitEvents();
    }

    public override void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create(() =>
        {
            if (Plugin is not null && ViewInstances.MainWindow is not null)
                new PluginDetailWindow()
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }
                .SetPluginInfo(Plugin.PluginInfo)
                .Show(ViewInstances.MainWindow);
        });

        RemoveCommand = ReactiveCommand.Create(() =>
        {
            if (Plugin is not null && PluginBar is not null)
            {
                PluginBars?.Remove(PluginBar);

                //PluginsNetwork.RequireRemovePlugin(PluginDetail);
            }
        });

        DeleteCommand = ReactiveCommand.Create(() =>
        {
            if (Plugin is not null && PluginBar is not null)
            {
                PluginBars?.Remove(PluginBar);
                //PluginsNetwork.RequireDeletePlugin(PluginDetail);
            }
        });

        LaunchCommand = ReactiveCommand.Create(() =>
        {
            var location = $"{nameof(PluginBarViewModel)}.{nameof(LaunchCommand)}";

            if (Plugin?.LoaderInfo is null) return;

            new Thread(() =>
            {
                try
                {
                    var loaderName = Plugin?.LoaderInfo.LoaderName;
                    var loaderVersion = Plugin?.LoaderInfo.LoaderVersion;
                    var pd = Plugin?.PluginInfo;

                    var pluginPath = $"{Plugin?.InstallPath}/{pd?.RootStartupFileName}";
                    var pluginFile = pluginPath.GetFullPath();
                    var connectStr = "ws://" +
                        $"{DevicesDiscoveryServer.Instance.DefaultDeviceInfo.Device.IPv4}" +
                        $":" +
                        $"{ConstantTable.PluginsServerPort}/";

                    if (Plugin is null) return;

                    if (Plugin.LoaderInfo.SelfLoad)
                        Process.Start(pluginFile, $"--connect {connectStr}");
                    else
                    {
                        var loaderFile = $"{AppConfig.Loaders.InstallPath}/" +
                            $"{loaderName}/{loaderVersion}/{loaderName}";

                        if (OperatingSystem.IsWindows())
                            loaderFile += ".exe";

                        loaderFile = loaderFile.GetFullPath();

                        Log.Information($"Launch: {pluginFile} through {loaderFile}");

                        if (File.Exists(loaderFile) && File.Exists(pluginFile))
                        {
                            var arg = $"--load \"{pluginFile}\" --connect {connectStr}";

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

    public override void InitEvents()
    {
        EventService.LanguageChanged += () => this.RaisePropertyChanged(nameof(DisplayName));
    }

    internal PluginBar? PluginBar { get; set; }

    internal PluginInstallation? Plugin { get; set; }

    internal string? DisplayName
    {
        get
        {
            if (Plugin is null) return null;

            return Plugin.PluginInfo.DisplayName.TryGetValue(
                AppConfig.App.AppLanguage, out var lang
            ) ? lang : Plugin.PluginInfo.DisplayName.Values.GetEnumerator().Current;
        }
    }

    internal string? AuthorName => Plugin?.PluginInfo.AuthorName;

    internal string? Version => Plugin?.PluginInfo.Version;

    internal ObservableCollection<PluginBar>? PluginBars { get; set; }

    internal Bitmap IconDisplay
    {
        get
        {
            var location = $"{nameof(PluginBarViewModel)}.{nameof(IconDisplay)}.getter";

            try
            {
                if (Plugin is null) return App.DefaultIcon;

                var src = Convert.FromBase64String(Plugin.PluginInfo.IconInBase64);

                using var ms = new MemoryStream(src);

                return new(ms);
            }
            catch (Exception e)
            {
                Log.Warning(
                    e,
                    $"In {location}: " +
                        $"Failed to transform icon from base64 to byte[] " +
                        $"or create bitmap from `MemoryStream`. {e.Message}"
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
