using Avalonia.Controls;
using Avalonia.Input;
using KitX.Dashboard.ViewModels.Pages.Controls;
using System.Collections.Generic;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Settings_About : UserControl
{
    private readonly Settings_AboutViewModel viewModel = new();

    public Settings_About()
    {
        InitializeComponent();

        DataContext = viewModel;

        viewModel.AppLogo = AppLogoItem;
    }
}
