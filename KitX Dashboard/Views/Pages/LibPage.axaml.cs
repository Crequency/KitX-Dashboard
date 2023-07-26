using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages;

namespace KitX_Dashboard.Views.Pages;

public partial class LibPage : UserControl
{
    private readonly LibPageViewModel libViewModel = new();

    public LibPage()
    {
        InitializeComponent();

        DataContext = libViewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
