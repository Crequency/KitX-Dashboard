using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class DevicesPage : UserControl
{
    private readonly DevicesPageViewModel viewModel = App.GetService<DevicesPageViewModel>();

    public DevicesPage()
    {
        InitializeComponent();

        DataContext = viewModel;

        // D11: the VM is DI-transient and recreated on every navigation — dispose its
        // event subscriptions when the page leaves the visual tree.
        Unloaded += (_, _) => viewModel.Dispose();
    }
}
