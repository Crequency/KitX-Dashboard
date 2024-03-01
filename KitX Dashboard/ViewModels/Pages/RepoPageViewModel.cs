using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Threading;
using Avalonia.Controls;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views.Pages;
using KitX.Dashboard.Views.Pages.Controls;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard.ViewModels.Pages;

internal class RepoPageViewModel : ViewModelBase
{
    private RepoPage? CurrentPage { get; set; }

    public RepoPageViewModel()
    {
        InitCommands();

        InitEvents();

        SearchingText = "";

        PluginsCount = PluginBars.Count.ToString();

        RefreshPluginsCommand?.Execute(new());
    }

    public override void InitCommands()
    {
        ImportPluginCommand = ReactiveCommand.Create<object?>(async win =>
        {
            if (win is not Window window) return;

            var topLevel = TopLevel.GetTopLevel(CurrentPage!);

            if (topLevel is null) return;

            var files = (await topLevel.StorageProvider.OpenFilePickerAsync(new()
            {
                Title = "Open KitX Extensions Package File",
                AllowMultiple = true,
            })).Select(x => x.Path.LocalPath).ToList().ToArray();

            if (files is not null && files?.Length > 0)
            {
                new Thread(() =>
                {
                    try
                    {
                        PluginsManager.ImportPlugin(files, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "In RepoPageViewModel.ImportPlugin()");
                    }
                }).Start();
            }

            RefreshPluginsCommand?.Execute(new());
        });

        RefreshPluginsCommand = ReactiveCommand.Create(() =>
        {
            PluginBars.Clear();

            //lock (PluginsNetwork.PluginsListOperationLock)
            //{

            //}

            foreach (var item in PluginsManager.Plugins)
            {
                try
                {
                    var plugin = new PluginInstallation()
                    {
                        InstallPath = item.InstallPath,
                        PluginInfo = JsonSerializer.Deserialize<PluginInfo>(
                            File.ReadAllText(
                                Path.GetFullPath($"{item.InstallPath}/PluginInfo.json")
                            )
                        ),
                        LoaderInfo = JsonSerializer.Deserialize<LoaderInfo>(
                            File.ReadAllText(
                                Path.GetFullPath($"{item.InstallPath}/LoaderInfo.json")
                            )
                        ),
                        InstalledDevices = []
                    };

                    PluginBars.Add(new(plugin, ref pluginBars));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "In RefreshPlugins()");
                }
            }
        });
    }

    internal RepoPageViewModel SetControl(RepoPage control)
    {
        CurrentPage = control;
        return this;
    }

    public override void InitEvents()
    {
        EventService.AppConfigChanged += () =>
        {
            ImportButtonVisibility = ConfigManager.Instance.AppConfig.App.DeveloperSetting;
        };

        PluginBars.CollectionChanged += (_, _) =>
        {
            PluginsCount = PluginBars.Count.ToString();
            NoPlugins_TipHeight = PluginBars.Count == 0 ? 300 : 0;
        };
    }

    internal string SearchingText { get; set; }

    private string pluginsCount = "0";

    internal string PluginsCount
    {
        get => pluginsCount;
        set
        {
            pluginsCount = value;

            this.RaisePropertyChanged(nameof(PluginsCount));
        }
    }

    private double noPlugins_tipHeight = 300;

    internal double NoPlugins_TipHeight
    {
        get => noPlugins_tipHeight;
        set
        {
            noPlugins_tipHeight = value;

            this.RaisePropertyChanged(nameof(NoPlugins_TipHeight));
        }
    }

    internal bool ImportButtonVisibility
    {
        get => ConfigManager.Instance.AppConfig.App.DeveloperSetting;
        set
        {
            ConfigManager.Instance.AppConfig.App.DeveloperSetting = value;

            this.RaisePropertyChanged(nameof(ImportButtonVisibility));
        }
    }

    private ObservableCollection<PluginBar> pluginBars = [];

    internal ObservableCollection<PluginBar> PluginBars
    {
        get => pluginBars;
        set => pluginBars = value;
    }

    internal ReactiveCommand<object?, Unit>? ImportPluginCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RefreshPluginsCommand { get; set; }
}
