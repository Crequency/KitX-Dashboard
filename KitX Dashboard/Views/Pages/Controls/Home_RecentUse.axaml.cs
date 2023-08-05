using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Controls;

public partial class Home_RecentUse : UserControl
{
    private readonly Home_RecentUseViewModel viewModel = new();

    public Home_RecentUse()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
