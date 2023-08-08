using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class MarketPage : UserControl
{
    private readonly MarketPageViewModel viewModel = new();

    public MarketPage()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
