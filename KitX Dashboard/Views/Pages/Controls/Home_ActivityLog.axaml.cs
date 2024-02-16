using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class Home_ActivityLog : UserControl
{
    private readonly Home_ActivityLogViewModel viewModel = new();

    public Home_ActivityLog()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
