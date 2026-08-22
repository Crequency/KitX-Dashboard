using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Developing : UserControl
{
    private static readonly DevelopingViewModel viewModel = App.GetService<DevelopingViewModel>();

    public Developing()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
