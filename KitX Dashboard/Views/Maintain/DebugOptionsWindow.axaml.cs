using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX.Dashboard.ViewModels.Maintain;

namespace KitX.Dashboard;

public partial class DebugOptionsWindow : Window
{
    private readonly DebugOptionsWindowViewModel viewModel = new();

    public DebugOptionsWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
