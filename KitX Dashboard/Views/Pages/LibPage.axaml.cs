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

        // D11: resubscribe on every attach — Unloaded disposes the subscription, and a
        // reused page instance (navigation cache) would otherwise stay frozen at its
        // first-render values. InitEvents is idempotent.
        Loaded += (_, _) => libViewModel.InitEvents();
        Unloaded += (_, _) => libViewModel.Dispose();
    }
}
