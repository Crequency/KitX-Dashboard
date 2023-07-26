using KitX_Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class Home_RecentUseViewModel : ViewModelBase
{

    public double NoRecent_TipHeight { get; set; } = 200;

    /// <summary>
    /// 插件卡片集合
    /// </summary>
    public ObservableCollection<PluginCard> RecentPluginCards { get; } = new();
}
