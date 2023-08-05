using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Data;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Models;
using KitX.Dashboard.Network;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Threading;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class PluginBarViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public PluginBarViewModel()
    {
        InitCommands();
        InitEvents();
    }

    internal void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create(() =>
        {
            if (PluginDetail is not null && Instances.MainWindow is not null)
                new PluginDetailWindow()
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }
                .SetPluginStruct(PluginDetail.PluginDetails)
                .Show(Instances.MainWindow);
        });

        RemoveCommand = ReactiveCommand.Create(() =>
        {
            if (PluginDetail is not null && PluginBar is not null)
            {
                PluginBars?.Remove(PluginBar);

                PluginsNetwork.RequireRemovePlugin(PluginDetail);
            }
        });

        DeleteCommand = ReactiveCommand.Create(() =>
        {
            if (PluginDetail is not null && PluginBar is not null)
            {
                PluginBars?.Remove(PluginBar);
                PluginsNetwork.RequireDeletePlugin(PluginDetail);
            }
        });

        LaunchCommand = ReactiveCommand.Create(() =>
        {
            var location = $"{nameof(PluginBarViewModel)}.{nameof(LaunchCommand)}";

            new Thread(() =>
            {
                try
                {
                    var loaderName = PluginDetail?.RequiredLoaderStruct.LoaderName;
                    var loaderVersion = PluginDetail?.RequiredLoaderStruct.LoaderVersion;
                    var pd = PluginDetail?.PluginDetails;

                    var pluginPath = $"{PluginDetail?.InstallPath}/{pd?.RootStartupFileName}";
                    var pluginFile = pluginPath.GetFullPath();
                    var connectStr = "" +
                        $"{DevicesDiscoveryServer.DefaultDeviceInfoStruct.IPv4}" +
                        $":" +
                        $"{GlobalInfo.PluginServerPort}";

                    if (PluginDetail is null) return;

                    if (PluginDetail.RequiredLoaderStruct.SelfLoad)
                        Process.Start(pluginFile, $"--connect {connectStr}");
                    else
                    {
                        var loaderFile = $"{ConfigManager.AppConfig.Loaders.InstallPath}/" +
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

    internal void InitEvents()
    {
        EventService.LanguageChanged += () =>
        {
            PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
        };
    }

    internal PluginBar? PluginBar { get; set; }

    internal Plugin? PluginDetail { get; set; }

    internal string? DisplayName
    {
        get
        {
            if (PluginDetail is null) return null;

            return PluginDetail.PluginDetails.DisplayName
                .ContainsKey(ConfigManager.AppConfig.App.AppLanguage)
                ? PluginDetail.PluginDetails.DisplayName[ConfigManager.AppConfig.App.AppLanguage]
                : PluginDetail.PluginDetails.DisplayName.Values.GetEnumerator().Current;
        }
    }

    internal string? AuthorName => PluginDetail?.PluginDetails.AuthorName;

    internal string? Version => PluginDetail?.PluginDetails.Version;

    internal ObservableCollection<PluginBar>? PluginBars { get; set; }

    internal Bitmap IconDisplay
    {
        get
        {
            var location = $"{nameof(PluginBarViewModel)}.{nameof(IconDisplay)}.getter";

            try
            {
                if (PluginDetail is null) return App.DefaultIcon;

                var src = Convert.FromBase64String(PluginDetail.PluginDetails.IconInBase64);

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
