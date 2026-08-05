using System.Collections.Specialized;
using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages;

namespace KitX.Dashboard.Views.Pages;

public partial class WorkflowPage : UserControl
{
    private readonly WorkflowPageViewModel workflowViewModel = App.GetService<WorkflowPageViewModel>();

    public WorkflowPage()
    {
        InitializeComponent();

        DataContext = workflowViewModel;

        // D5: the VM is DI-transient and recreated on every navigation — dispose its
        // event subscriptions when the page leaves the visual tree, so the singleton
        // EventService never accumulates references to dead page instances.
        Unloaded += (_, _) => workflowViewModel.Dispose();

        // Auto-scroll the activity log to the newest line as items arrive.
        // Hooking here (rather than in the VM) keeps UI concerns out of the VM and lets
        // us reach the ListBox's internal ScrollViewer cleanly.
        if (workflowViewModel.ExecutionLog is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset) return;
                if (ActivityLogList?.ItemCount is > 0)
                {
                    // Scroll to the last item; Avalonia's ListBox.ScrollIntoView is 0-based.
                    ActivityLogList.ScrollIntoView(ActivityLogList.ItemCount - 1);
                }
            };
        }
    }
}
