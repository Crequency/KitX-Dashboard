using KitX.Dashboard.Models;
using KitX.Dashboard.Views;
using System.Collections.ObjectModel;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_RecentUseViewModel : ViewModelBase, IView
{
    public Home_RecentUseViewModel()
    {

    }

    public double NoRecent_TipHeight { get; set; } = 200;

    public ObservableCollection<Plugin> RecentPlugins { get; } = [];

    public override void InitCommands() => throw new System.NotImplementedException();

    public override void InitEvents() => throw new System.NotImplementedException();
}
