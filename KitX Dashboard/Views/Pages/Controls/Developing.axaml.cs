using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Developing : UserControl
{
    private static readonly DevelopingViewModel viewModel = new();

    public Developing()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
