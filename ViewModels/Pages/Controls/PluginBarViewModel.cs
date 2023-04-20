using Avalonia.Controls;
using Avalonia.Media.Imaging;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Models;
using KitX_Dashboard.Servers;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views;
using KitX_Dashboard.Views.Pages.Controls;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class PluginBarViewModel : ViewModelBase, INotifyPropertyChanged
{
    public PluginBarViewModel()
    {
        InitCommands();
        InitEvents();
    }

    internal void InitCommands()
    {
        ViewDetailsCommand = new(ViewDetails);
        RemoveCommand = new(Remove);
        DeleteCommand = new(Delete);
        LaunchCommand = new(Launch);
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
            if (PluginDetail != null)
                return PluginDetail.PluginDetails.DisplayName
                    .ContainsKey(Program.Config.App.AppLanguage)
                    ? PluginDetail.PluginDetails.DisplayName[Program.Config.App.AppLanguage]
                    : PluginDetail.PluginDetails.DisplayName.Values.GetEnumerator().Current;
            return null;
        }
    }

    internal string? AuthorName => PluginDetail?.PluginDetails.AuthorName;

    internal string? Version => PluginDetail?.PluginDetails.Version;

    internal ObservableCollection<PluginBar>? PluginBars { get; set; }

    internal Bitmap IconDisplay
    {
        get
        {
            try
            {
                if (PluginDetail != null)
                {
                    byte[] src = Convert.FromBase64String(PluginDetail.PluginDetails.IconInBase64);
                    using var ms = new MemoryStream(src);
                    return new(ms);
                }
                else return App.DefaultIcon;
            }
            catch (Exception e)
            {
                Log.Warning(e, $"Icon transform error from base64 to byte[] or " +
                    $"create bitmap from MemoryStream error: {e.Message}");
                return App.DefaultIcon;
            }
        }
    }

    internal DelegateCommand? ViewDetailsCommand { get; set; }

    internal DelegateCommand? RemoveCommand { get; set; }

    internal DelegateCommand? DeleteCommand { get; set; }

    internal DelegateCommand? LaunchCommand { get; set; }

    /// <summary>
    /// 查看详细信息
    /// </summary>
    /// <param name="_"></param>
    internal void ViewDetails(object _)
    {
        if (PluginDetail != null && Program.MainWindow != null)
            new PluginDetailWindow()
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            }
            .SetPluginStruct(PluginDetail.PluginDetails)
            .Show(Program.MainWindow);
    }

    /// <summary>
    /// 移除
    /// </summary>
    /// <param name="_"></param>
    internal void Remove(object _)
    {
        if (PluginDetail != null && PluginBar != null)
        {
            PluginBars?.Remove(PluginBar);
            PluginsManager.RequireRemovePlugin(PluginDetail);
        }
    }

    /// <summary>
    /// 删除
    /// </summary>
    /// <param name="_"></param>
    internal void Delete(object _)
    {
        if (PluginDetail != null && PluginBar != null)
        {
            PluginBars?.Remove(PluginBar);
            PluginsManager.RequireDeletePlugin(PluginDetail);
        }
    }

    /// <summary>
    /// 启动
    /// </summary>
    /// <param name="_"></param>
    internal void Launch(object _)
    {
        new Thread(() =>
        {
            try
            {
                var loaderName = PluginDetail?.RequiredLoaderStruct.LoaderName;
                var pd = PluginDetail?.PluginDetails;
                string pluginPath = $"{PluginDetail?.InstallPath}/{pd?.RootStartupFileName}";
                string pluginFile = Path.GetFullPath(pluginPath);
                string connectStr = "" +
                    $"{DevicesDiscoveryServer.DefaultDeviceInfoStruct.IPv4}" +
                    $":" +
                    $"{GlobalInfo.PluginServerPort}";
                if (PluginDetail != null && PluginDetail.RequiredLoaderStruct.SelfLoad)
                    Process.Start(pluginFile, $"--connect {connectStr}");
                else
                {
                    string loaderFile = $"{Program.Config.Loaders.InstallPath}/{loaderName}/{loaderName}";
                    if (OperatingSystem.IsWindows())
                        loaderFile += ".exe";
                    loaderFile = Path.GetFullPath(loaderFile);

                    Log.Information($"Launch: {pluginFile} through {loaderFile}");

                    if (File.Exists(loaderFile) && File.Exists(pluginFile))
                    {
                        string arg = $"--load \"{pluginFile}\" --connect {connectStr}";
                        Log.Information($"Launch Argument: {arg}");
                        Process.Start(loaderFile, arg);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "In PluginBarViewModel.Launch()");
            }
        }).Start();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
}
