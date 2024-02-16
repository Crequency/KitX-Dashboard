using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace KitX.Dashboard.ViewModels.Pages;

internal class LibPageViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public LibPageViewModel()
    {
        PluginCards.CollectionChanged += (_, _) =>
        {
            NoPlugins_TipHeight = PluginCards.Count == 0 ? 300 : 0;
            PluginsCount = $"{PluginCards.Count}";
        };
    }

    public override void InitCommands() => throw new System.NotImplementedException();

    public override void InitEvents() => throw new System.NotImplementedException();

    public string pluginsCount = $"{PluginCards.Count}";

    public string PluginsCount
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

    public double noPlugins_tipHeight = PluginCards.Count == 0 ? 300 : 0;

    public double NoPlugins_TipHeight
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

    public static ObservableCollection<PluginCard> PluginCards => ViewInstances.PluginCards;

    public string? SearchingText { get; set; }
}
