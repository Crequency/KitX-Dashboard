using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Settings_General : UserControl
{
    private readonly Settings_GeneralViewModel viewModel = App.GetService<Settings_GeneralViewModel>();

    public Settings_General()
    {
        InitializeComponent();

        DataContext = viewModel;

        // D11: the VM is transient per view and subscribes to the singleton event bus —
        // dispose it when the view leaves the visual tree (SettingsFrame re-navigates
        // between sub-views, which would otherwise leak one subscription per navigation).
        Unloaded += (_, _) => viewModel.Dispose();
    }
}
