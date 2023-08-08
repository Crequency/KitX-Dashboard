using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class DevicePage : UserControl
{
    private readonly DevicePageViewModel viewModel = new();

    public DevicePage()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
