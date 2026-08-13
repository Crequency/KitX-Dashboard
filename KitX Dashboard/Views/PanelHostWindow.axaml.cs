using Avalonia.Controls;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// The ToolKit <b>use surface</b> window: lists every instance across mounted ToolKits and
/// lets the user end them. Each instance is a runtime incarnation of a mounted ToolKit
/// (ToolKit 实例模型定稿) — the panel view and run monitor live here, distinct from the
/// Bench design surface. Opening it from the Tray or hotkey shows/hides this window.
/// </summary>
public partial class PanelHostWindow : Window
{
    private readonly PanelHostViewModel viewModel = App.GetService<PanelHostViewModel>();

    public PanelHostWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    /// <summary>Number control value change → write-back via the control's command.</summary>
    private void OnNumberValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (sender is NumericUpDown nud && nud.DataContext is PanelControlViewModel vm && nud.Value is decimal d)
            vm.NumberCommand?.Execute((double)d);
    }

    /// <summary>Select control selection change → write-back via the control's command.</summary>
    private void OnSelectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is PanelControlViewModel vm && cb.SelectedItem is string s)
            vm.SelectCommand?.Execute(s);
    }
}
