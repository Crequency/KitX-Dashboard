using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls;

public partial class Settings_General : UserControl
{
    private readonly Settings_GeneralViewModel viewModel = new();

    public Settings_General()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
