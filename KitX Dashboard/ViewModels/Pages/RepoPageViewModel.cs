using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Plugin;
using KitX.Dashboard;
using KitX.Dashboard.Views.Pages;
using KitX.Dashboard.Views.Pages.Controls;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard.ViewModels.Pages;

internal class RepoPageViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IEventService _eventService;
    private readonly IPluginService _pluginService;
    private RepoPage? CurrentPage { get; set; }

    /// <summary>Named handler so <see cref="Cleanup"/> can unsubscribe it (D11).</summary>
    private readonly EventHandler<EventArgs> _appConfigChangedHandler;

    public RepoPageViewModel(IConfigService configService, IEventService eventService, IPluginService pluginService)
    {
        _configService = configService;
        _eventService = eventService;
        _pluginService = pluginService;

        _appConfigChangedHandler = (_, _) => ImportButtonVisibility = _configService.AppConfig.App.DeveloperSetting;

        InitCommands();

        InitEvents();

        SearchingText = "";

        PluginsCount = PluginBars.Count.ToString();
    }

    public sealed override void InitCommands()
    {
        ImportPluginCommand = ReactiveCommand.Create<object?>(async obj =>
        {
            // Try to get TopLevel from the current page first, then from the command parameter
            var topLevel = CurrentPage is not null
                ? TopLevel.GetTopLevel(CurrentPage)
                : null;

            // If CurrentPage doesn't work, try the obj parameter
            if (topLevel is null && obj is Window window)
            {
                topLevel = TopLevel.GetTopLevel(window);
            }

            // Last resort: try to get the main window from Application
            if (topLevel is null && Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            }

            if (topLevel is null)
            {
                Log.Warning("Cannot get TopLevel for file picker");
                return;
            }

            var files = (
                await topLevel.StorageProvider.OpenFilePickerAsync(
                    new() { Title = "Open KitX Extensions Package File", AllowMultiple = true }
                )
            )
                .Select(x => x.Path.LocalPath)
                .ToList()
                .ToArray();

            if (files is not null && files?.Length > 0)
            {
                try
                {
                    foreach (var file in files!)
                    {
                        await _pluginService.ImportPluginAsync(file);
                    }
                    // Import completed, refresh the list
                    RefreshPluginsCommand?.Execute(new());
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "In RepoPageViewModel.ImportPlugin()");
                }
            }
        });

        RefreshPluginsCommand = ReactiveCommand.Create(PerformRefresh);
    }

    internal RepoPageViewModel SetControl(RepoPage control)
    {
        CurrentPage = control;
        return this;
    }

    public sealed override void InitEvents()
    {
        _eventService.Subscribe(EventNames.AppConfigChanged, _appConfigChangedHandler);

        // Subscribe to plugin status changes for runtime auto-refresh
        _pluginService.PluginStatusChanged += OnPluginStatusChanged;

        PluginBars.CollectionChanged += (_, _) =>
        {
            PluginsCount = PluginBars.Count.ToString();
            ApplyFilter();
        };
    }

    private DateTime _lastRefreshTime = DateTime.MinValue;
    private static readonly TimeSpan RefreshDebounceInterval = TimeSpan.FromMilliseconds(300);

    private void OnPluginStatusChanged(object? sender, PluginStatusChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (DateTime.Now - _lastRefreshTime < RefreshDebounceInterval)
                return;
            _lastRefreshTime = DateTime.Now;
            PerformRefresh();
        });
    }

    /// <summary>
    /// Unsubscribes event handlers to prevent memory leaks.
    /// Called from RepoPage.Unloaded.
    /// </summary>
    internal void Cleanup()
    {
        _pluginService.PluginStatusChanged -= OnPluginStatusChanged;
        _eventService.Unsubscribe(EventNames.AppConfigChanged, _appConfigChangedHandler);
    }

    /// <summary>
    /// Refreshes the plugin list from the plugin service.
    /// File IO + JSON deserialization runs on the thread pool (D12); the <see cref="PluginBar"/>
    /// controls themselves are constructed back on the UI thread (Avalonia controls are
    /// not thread-affine-safe to build off the UI thread).
    /// Called from Loaded (bypasses ReactiveCommand scheduling) and from RefreshPluginsCommand.
    /// </summary>
    internal async void PerformRefresh()
    {
        PluginBars.Clear();

        var installations = await Task.Run(() =>
        {
            var list = new List<PluginInstallation>();

            foreach (var item in _pluginService.GetInstalledPlugins())
            {
                try
                {
                    list.Add(new PluginInstallation()
                    {
                        Id = item.Id,
                        InstallPath = item.InstallPath,
                        PluginInfo = JsonSerializer.Deserialize<PluginInfo>(
                            File.ReadAllText(Path.GetFullPath($"{item.InstallPath}/PluginInfo.json"))
                        ),
                        LoaderInfo = JsonSerializer.Deserialize<LoaderInfo>(
                            File.ReadAllText(Path.GetFullPath($"{item.InstallPath}/LoaderInfo.json"))
                        ),
                        InstalledDevices = [],
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "In RefreshPlugins()");
                }
            }

            return list;
        });

        foreach (var plugin in installations)
            PluginBars.Add(new(plugin, ref pluginBars));
    }

    private string _searchingText = "";

    internal string SearchingText
    {
        get => _searchingText;
        set
        {
            if (_searchingText == value) return;
            _searchingText = value ?? "";
            ApplyFilter();
        }
    }

    /// <summary>
    /// Filtered view of <see cref="PluginBars"/> bound by the page's plugin list.
    /// Matches plugin name or ID, ignoring case; an empty keyword shows all.
    /// </summary>
    private readonly ObservableCollection<PluginBar> _displayedPluginBars = [];

    internal ObservableCollection<PluginBar> DisplayedPluginBars => _displayedPluginBars;

    private void ApplyFilter()
    {
        var keyword = _searchingText?.Trim() ?? string.Empty;

        _displayedPluginBars.Clear();

        foreach (var bar in pluginBars)
        {
            var plugin = bar.Plugin;
            var info = plugin?.PluginInfo;

            if (keyword.Length == 0
                || info?.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || plugin?.Id.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
                _displayedPluginBars.Add(bar);
        }

        NoPlugins_TipHeight = _displayedPluginBars.Count == 0 ? 300 : 0;
    }

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
        get => _configService.AppConfig.App.DeveloperSetting;
        set
        {
            _configService.AppConfig.App.DeveloperSetting = value;

            this.RaisePropertyChanged(nameof(ImportButtonVisibility));

            _configService.SaveAll();
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
