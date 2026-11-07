using KitX.Dashboard.ViewModels;
using Avalonia.Controls;

namespace KitX.Dashboard.Views;

/// <summary>
/// The card orchestrator (implementation design P5): a fixed 390 × 845 grid for one ToolKit.
/// </summary>
/// <remarks>
/// The window knows nothing about layouts — it hands the view model a ToolKit id and lets it read
/// and write. Everything the window does beyond that is chrome.
/// </remarks>
public partial class SurfaceEditorWindow : Window
{
    /// <summary>Creates the window. The view model must be supplied before showing.</summary>
    public SurfaceEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>Creates the window and loads a ToolKit's layout.</summary>
    /// <param name="viewModel">The editor's view model.</param>
    /// <param name="toolkitId">The ToolKit to edit.</param>
    public SurfaceEditorWindow(SurfaceEditorViewModel viewModel, string toolkitId) : this()
    {
        DataContext = viewModel;
        viewModel.Load(toolkitId);
    }
}
