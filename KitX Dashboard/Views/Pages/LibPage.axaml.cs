using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class LibPage : UserControl
{
    private readonly LibPageViewModel libViewModel = new();

    public LibPage()
    {
        InitializeComponent();

        DataContext = libViewModel;
    }
}
