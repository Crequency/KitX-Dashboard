using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class LibPage : UserControl
{
    private readonly LibPageViewModel libViewModel = App.GetService<LibPageViewModel>();

    public LibPage()
    {
        InitializeComponent();

        DataContext = libViewModel;

        // D11: the VM is recreated on every navigation — dispose its subscriptions
        // when the page leaves the visual tree.
        Unloaded += (_, _) => libViewModel.Dispose();
    }
}
