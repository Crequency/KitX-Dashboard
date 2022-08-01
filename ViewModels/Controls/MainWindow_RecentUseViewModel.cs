using KitX_Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;

namespace KitX_Dashboard.ViewModels.Controls
{
    internal class MainWindow_RecentUseViewModel : ViewModelBase
    {

        public double NoRecent_TipHeight { get; set; } = 200;

        /// <summary>
        /// �����Ƭ����
        /// </summary>
        public ObservableCollection<PluginCard> RecentPluginCards { get; } = new();
    }
}
