using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Controls;

public partial class Home_Count : UserControl
{
    private readonly Home_CountViewModel viewModel = new();

    public Home_Count()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
