using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;
using KitX.Web.Rules;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class DeviceCard : UserControl
{
    internal readonly DeviceCardViewModel viewModel = new();

    public DeviceCard()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    public DeviceCard(DeviceInfoStruct deviceInfo)
    {
        InitializeComponent();

        viewModel.DeviceInfo = deviceInfo;

        DataContext = viewModel;
    }
}
