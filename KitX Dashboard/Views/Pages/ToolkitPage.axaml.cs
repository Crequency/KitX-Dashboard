using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class ToolkitPage : UserControl
{
    private readonly ToolkitPageViewModel viewModel = App.GetService<ToolkitPageViewModel>();

    public ToolkitPage()
    {
        InitializeComponent();

        DataContext = viewModel;

        // D11: the VM is DI-transient and recreated on every navigation.
        Unloaded += (_, _) => viewModel.Dispose();
    }
}
