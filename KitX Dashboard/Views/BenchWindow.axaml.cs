using Avalonia.Controls;
using Avalonia.Input;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// The Bench orchestration window (the "workbench" — the design surface). Hosts the
/// editable NodifyM canvas over the in-memory ToolKit config, the palette, and the
/// property inspector. Delete key removes the selected node(s).
/// </summary>
public partial class BenchWindow : Window
{
    private readonly BenchViewModel viewModel = App.GetService<BenchViewModel>();

    public BenchWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;
        if (IsTextInputFocused())
            return;

        viewModel.Canvas?.DeleteSelectedNodesCommand.Execute(null);
        e.Handled = true;
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager?.GetFocusedElement();
        return focused is TextBox or ComboBox or NumericUpDown;
    }
}
