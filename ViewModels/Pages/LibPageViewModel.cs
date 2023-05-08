﻿using KitX_Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages;

internal class LibPageViewModel : ViewModelBase, INotifyPropertyChanged
{
    public LibPageViewModel()
    {
        PluginCards.CollectionChanged += (_, _) =>
        {
            NoPlugins_TipHeight = PluginCards.Count == 0 ? 300 : 0;
            PluginsCount = $"{PluginCards.Count}";
        };
    }

    public string pluginsCount = $"{PluginCards.Count}";

    public string PluginsCount
    {
        get => pluginsCount;
        set
        {
            pluginsCount = value;
            PropertyChanged?.Invoke(this, new(nameof(PluginsCount)));
        }
    }

    public double noPlugins_tipHeight = PluginCards.Count == 0 ? 300 : 0;

    public double NoPlugins_TipHeight
    {
        get => noPlugins_tipHeight;
        set
        {
            noPlugins_tipHeight = value;
            PropertyChanged?.Invoke(this, new(nameof(NoPlugins_TipHeight)));
        }
    }

    /// <summary>
    /// 插件卡片集合
    /// </summary>
    public static ObservableCollection<PluginCard> PluginCards { get => Program.PluginCards; }

    /// <summary>
    /// 搜索框文字
    /// </summary>
    public string? SearchingText { get; set; }

    public new event PropertyChangedEventHandler? PropertyChanged;
}
