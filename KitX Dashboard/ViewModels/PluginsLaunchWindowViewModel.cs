using Avalonia.Controls;
using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace KitX.Dashboard.ViewModels;

internal class PluginsLaunchWindowViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public PluginsLaunchWindowViewModel()
    {
        InitEvents();
    }

    public override void InitCommands() => throw new System.NotImplementedException();

    public override void InitEvents()
    {
        PluginLaunchCards.CollectionChanged += (_, _) =>
        {
            PluginsCount = $"{PluginLaunchCards.Count}";
        };
    }

    internal void BuildLauncherMenu(PluginsLaunchWindow window)
    {
        var menu = window.FindControl<StackPanel>("Menu");

        menu?.Children.Clear();

        foreach (var card in PluginLaunchCards)
        {
            ;
        }
    }

    public string pluginsCount = $"0";

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

    //public ObservableCollection<PluginLaunchCard> PluginLaunchCards = [];
    public static ObservableCollection<PluginLaunchCard> PluginLaunchCards => ViewInstances.PluginLaunchCards;


    public string? SearchingText { get; set; }
}
