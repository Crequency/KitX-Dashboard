using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Settings_General : UserControl
{
    private readonly Settings_GeneralViewModel viewModel = new();

    public Settings_General()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
