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

        // D11 symmetry with LibPage: Unloaded disposes the VM's subscriptions; if the
        // Frame reuses this page instance (navigation cache), Loaded must resubscribe —
        // otherwise the rebound page stays frozen at its first-visit values.
        Loaded += (_, _) => viewModel.InitEvents();
        Unloaded += (_, _) => viewModel.Dispose();
    }
}
