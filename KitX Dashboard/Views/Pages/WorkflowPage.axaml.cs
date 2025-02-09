using System;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Managers;
using KitX.Dashboard.ViewModels.Pages;
using KitX.Dashboard.Views.Pages.Controls;
using Serilog;

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
