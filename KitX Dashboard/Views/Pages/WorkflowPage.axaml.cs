using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;


namespace KitX.Dashboard.Views.Pages;

public partial class WorkflowPage : UserControl
{
    private readonly WorkflowPageViewModel workflowViewModel = new();

    public WorkflowPage()
    {
        InitializeComponent();

        DataContext = workflowViewModel;
    }
}
