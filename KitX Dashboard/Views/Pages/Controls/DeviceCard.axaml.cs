using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;
using KitX.Shared.Device;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class DeviceCard : UserControl
{
    internal readonly DeviceCardViewModel viewModel = new();

    public DeviceCard()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    public DeviceCard(DeviceInfo deviceInfo)
    {
        InitializeComponent();

        viewModel.DeviceInfo = deviceInfo;

        DataContext = viewModel;
    }
}
