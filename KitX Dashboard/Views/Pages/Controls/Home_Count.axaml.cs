using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Controls;

public partial class Home_Count : UserControl
{
    private readonly Home_CountViewModel viewModel = new();

    public Home_Count()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
