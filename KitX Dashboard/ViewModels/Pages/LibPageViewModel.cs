using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Plugin;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class LibPageViewModel : ViewModelBase, IDisposable
{
    /// <summary>Named handler so <see cref="Dispose"/> can unsubscribe it (D11).</summary>
    private readonly NotifyCollectionChangedEventHandler _pluginInfosChangedHandler;

    public LibPageViewModel()
    {
        _pluginInfosChangedHandler = (_, _) =>
        {
            ApplyFilter();
            PluginsCount = $"{PluginInfos.Count}";
        };

        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create<PluginInfo>(info =>
        {
            if (UIStateService.MainWindow is not null)
                new PluginDetailWindow() { WindowStartupLocation = WindowStartupLocation.CenterOwner }
                    .SetPluginInfo(info)
                    .Show(UIStateService.MainWindow);
        });
    }

    public sealed override void InitEvents()
    {
        PluginInfos.CollectionChanged += _pluginInfosChangedHandler;
    }

    /// <summary>
    /// Unsubscribes every subscription made in <see cref="InitEvents"/> (D11).
    /// </summary>
    public void Dispose()
    {
        PluginInfos.CollectionChanged -= _pluginInfosChangedHandler;
    }

    private string? _searchingText;

    public string? SearchingText
    {
        get => _searchingText;
        set
        {
            if (_searchingText == value) return;
            _searchingText = value;
            ApplyFilter();
        }
    }

    /// <summary>
    /// Filtered view of <see cref="PluginInfos"/> bound by the page's plugin grid.
    /// Matches plugin name / author / any localized display name, ignoring case;
    /// an empty keyword shows all.
    /// </summary>
    private readonly ObservableCollection<PluginInfo> _displayedPluginInfos = [];

    public ObservableCollection<PluginInfo> DisplayedPluginInfos => _displayedPluginInfos;

    private void ApplyFilter()
    {
        var keyword = _searchingText?.Trim() ?? string.Empty;

        _displayedPluginInfos.Clear();

        foreach (var plugin in PluginInfos)
        {
            if (keyword.Length == 0
                || plugin.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || plugin.AuthorName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || plugin.DisplayName.Values.Any(v => v.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                _displayedPluginInfos.Add(plugin);
        }

        NoPlugins_TipHeight = _displayedPluginInfos.Count == 0 ? 300 : 0;
    }

    public string pluginsCount = $"{PluginInfos.Count}";

    public string PluginsCount
    {
        get => pluginsCount;
        set => this.RaiseAndSetIfChanged(ref pluginsCount, value);
    }

    public double noPlugins_tipHeight = PluginInfos.Count == 0 ? 300 : 0;

    public double NoPlugins_TipHeight
    {
        get => noPlugins_tipHeight;
        set => this.RaiseAndSetIfChanged(ref noPlugins_tipHeight, value);
    }

    public static ObservableCollection<PluginInfo> PluginInfos => UIStateService.PluginInfos;

    internal ReactiveCommand<PluginInfo, Unit>? ViewDetailsCommand { get; set; }
}
