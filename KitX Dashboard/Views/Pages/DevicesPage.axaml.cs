using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;
using Serilog;

namespace KitX.Dashboard.Views.Pages;

public partial class DevicesPage : UserControl
{
    private readonly DevicesPageViewModel viewModel = App.GetService<DevicesPageViewModel>();

    public DevicesPage()
    {
        InitializeComponent();

        DataContext = viewModel;

        // D11 symmetry with LibPage: Unloaded disposes the VM's subscriptions; if the
        // Frame reuses this page instance (navigation cache), Loaded must resubscribe —
        // otherwise the rebound page stays frozen at its first-visit values.
        Loaded += (_, _) =>
        {
            Log.Information($"[DevDiag] Page#{GetHashCode():X8} Loaded: DataContext VM#{(DataContext as DevicesPageViewModel)?.GetHashCode().ToString("X8") ?? "null"}");
            viewModel.InitEvents();
        };
        Unloaded += (_, _) =>
        {
            Log.Information($"[DevDiag] Page#{GetHashCode():X8} Unloaded: DataContext VM#{(DataContext as DevicesPageViewModel)?.GetHashCode().ToString("X8") ?? "null"}");
            viewModel.Dispose();
        };
    }
}
