using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Settings_Personalise : UserControl
{
    private readonly Settings_PersonaliseViewModel viewModel = App.GetService<Settings_PersonaliseViewModel>();

    public Settings_Personalise()
    {
        InitializeComponent();

        DataContext = viewModel;

        // D11: the VM is recreated on each navigation — dispose its event subscriptions
        // when the page leaves the visual tree.
        Unloaded += (_, _) => viewModel.Dispose();
    }
}
