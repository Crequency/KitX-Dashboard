using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Home_RecentUse : UserControl
{
    private readonly Home_RecentUseViewModel viewModel = App.GetService<Home_RecentUseViewModel>();

    public Home_RecentUse()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
