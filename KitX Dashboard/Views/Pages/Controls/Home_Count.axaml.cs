using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Home_Count : UserControl
{
    private readonly Home_CountViewModel viewModel = App.GetService<Home_CountViewModel>();

    public Home_Count()
    {
        InitializeComponent();

        DataContext = viewModel;

        Unloaded += (_, _) => viewModel.Dispose();
    }
}
