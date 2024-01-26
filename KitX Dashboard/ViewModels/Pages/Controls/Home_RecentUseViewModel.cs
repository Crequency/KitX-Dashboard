using KitX.Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_RecentUseViewModel : ViewModelBase
{

    public double NoRecent_TipHeight { get; set; } = 200;

    public ObservableCollection<PluginCard> RecentPluginCards { get; } = new();
}
