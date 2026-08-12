using Avalonia.Controls;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// The Bench orchestration window (the "workbench"). Scaffolding only ("not officially
/// released"): the DataContext is the transient <see cref="BenchViewModel"/>, which reads
/// the currently-activated ToolKit config. The node canvas is a later GUI iteration.
/// </summary>
public partial class BenchWindow : Window
{
    private readonly BenchViewModel viewModel = App.GetService<BenchViewModel>();

    public BenchWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
