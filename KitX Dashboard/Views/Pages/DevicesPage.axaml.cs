using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class DevicesPage : UserControl
{
    private readonly DevicesPageViewModel viewModel = new();

    public DevicesPage()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
