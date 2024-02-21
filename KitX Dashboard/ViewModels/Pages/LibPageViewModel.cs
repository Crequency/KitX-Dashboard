using Avalonia.Controls;
using KitX.Dashboard.Views;
using KitX.Shared.Plugin;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;

namespace KitX.Dashboard.ViewModels.Pages;

internal class LibPageViewModel : ViewModelBase
{
    public LibPageViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create<PluginInfo>(info =>
        {
            if (ViewInstances.MainWindow is not null)
                new PluginDetailWindow()
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }
                .SetPluginInfo(info)
                .Show(ViewInstances.MainWindow);
        });
    }

    public override void InitEvents()
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

    public static ObservableCollection<PluginInfo> PluginInfos => ViewInstances.PluginInfos;

    internal ReactiveCommand<PluginInfo, Unit>? ViewDetailsCommand { get; set; }
}
