using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;
using Serilog;

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
        Loaded += (_, _) =>
        {
            Log.Information($"[LibDiag] Page#{GetHashCode():X8} Loaded: DataContext VM#{(DataContext as LibPageViewModel)?.GetHashCode().ToString("X8") ?? "null"}");
            libViewModel.InitEvents();
        };
        Unloaded += (_, _) =>
        {
            Log.Information($"[LibDiag] Page#{GetHashCode():X8} Unloaded: DataContext VM#{(DataContext as LibPageViewModel)?.GetHashCode().ToString("X8") ?? "null"}");
            libViewModel.Dispose();
        };
    }
}
