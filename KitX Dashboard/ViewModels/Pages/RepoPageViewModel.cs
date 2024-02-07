using Avalonia.Controls;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views.Pages;
using KitX.Dashboard.Views.Pages.Controls;
using KitX.Shared.Loader;
using KitX.Shared.Plugin;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Threading;

namespace KitX.Dashboard.ViewModels.Pages;

internal class RepoPageViewModel : ViewModelBase, INotifyPropertyChanged
{
    private RepoPage? CurrentPage { get; set; }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public RepoPageViewModel()
    {
        InitCommands();

        InitEvents();

        SearchingText = "";

        PluginsCount = PluginBars.Count.ToString();

        RefreshPluginsCommand?.Execute(new());
    }

    private void InitCommands()
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

            lock (PluginsNetwork.PluginsListOperationLock)
            {
                foreach (var item in PluginsManager.Plugins)
                {
                    try
                    {
                        var plugin = new Plugin()
                        {
                            InstallPath = item.InstallPath,
                            PluginDetails = JsonSerializer.Deserialize<PluginInfo>(
                                File.ReadAllText(
                                    Path.GetFullPath($"{item.InstallPath}/PluginInfo.json")
                                )
                            ),
                            RequiredLoaderInfo = JsonSerializer.Deserialize<LoaderInfo>(
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
            }
        });
    }

    internal RepoPageViewModel SetControl(RepoPage control)
    {
        CurrentPage = control;
        return this;
    }

    private void InitEvents()
    {
        EventService.ConfigSettingsChanged += () =>
        {
            ImportButtonVisibility = ConfigManager.AppConfig.App.DeveloperSetting;
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

            PropertyChanged?.Invoke(
                this,
                new(nameof(PluginsCount))
            );
        }
    }

    private double noPlugins_tipHeight = 300;

    internal double NoPlugins_TipHeight
    {
        get => noPlugins_tipHeight;
        set
        {
            noPlugins_tipHeight = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(NoPlugins_TipHeight))
            );
        }
    }

    internal bool ImportButtonVisibility
    {
        get => ConfigManager.AppConfig.App.DeveloperSetting;
        set
        {
            ConfigManager.AppConfig.App.DeveloperSetting = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(ImportButtonVisibility))
            );
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
