using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class AccountPage : UserControl
{
    private static readonly AccountPageViewModel viewModel = new();

    public AccountPage()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
