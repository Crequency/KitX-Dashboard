using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Controls;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Plugin;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class LibPageViewModel : ViewModelBase
{
    public LibPageViewModel()
    {
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
        PluginInfos.CollectionChanged += (_, args) =>
        {
            NoPlugins_TipHeight = PluginInfos.Count == 0 ? 300 : 0;
            PluginsCount = $"{PluginInfos.Count}";
        };
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

    public string? SearchingText { get; set; }

    public static ObservableCollection<PluginInfo> PluginInfos => UIStateService.PluginInfos;

    internal ReactiveCommand<PluginInfo, Unit>? ViewDetailsCommand { get; set; }
}
