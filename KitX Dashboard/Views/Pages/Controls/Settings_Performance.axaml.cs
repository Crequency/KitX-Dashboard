using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Settings_Performance : UserControl
{
    private readonly Settings_PerformanceViewModel viewModel = App.GetService<Settings_PerformanceViewModel>();

    public Settings_Performance()
    {
        InitializeComponent();

        DataContext = viewModel;

        // D11: the VM is DI-transient and recreated on each navigation — dispose its
        // event subscriptions when the page leaves the visual tree.
        Unloaded += (_, _) => viewModel.Dispose();
    }
}
