using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Settings_Performence : UserControl
{
    private readonly Settings_PerformenceViewModel viewModel = App.GetService<Settings_PerformenceViewModel>();

    public Settings_Performence()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
