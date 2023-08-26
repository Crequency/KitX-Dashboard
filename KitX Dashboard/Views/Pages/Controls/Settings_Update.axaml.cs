using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Settings_Update : UserControl
{
    private readonly Settings_UpdateViewModel viewModel = new();

    public Settings_Update()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
