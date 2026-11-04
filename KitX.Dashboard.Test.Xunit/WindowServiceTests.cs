using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using KitX.Dashboard.Services;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Window-service unit tests (post DI migration of the retired static window coordinator).
/// <para>
/// The service's <see cref="WindowService"/> constructor-injects nothing (no ViewModels —
/// a VM dependency there would create the DI cycle that surfaces as a frozen UI), so it can
/// be constructed directly in headless tests. Real windows that resolve their DataContext via
/// the DI container (BenchWindow / WorkflowEditorWindowV6) cannot be instantiated here, so the
/// <see cref="WindowService.RegisterWorkflowEditor"/> seam is the tested surface.
/// </para>
/// </summary>
public class WindowServiceTests
{
    [AvaloniaFact]
    public void WorkflowEditorWindows_Registers_And_Removes_On_Close()
    {
        var svc = new WindowService();
        var window = new Window();
        svc.RegisterWorkflowEditor("wf-1", window);

        Assert.True(svc.WorkflowEditorWindows.ContainsKey("wf-1"));
        Assert.Same(window, svc.WorkflowEditorWindows["wf-1"]);

        // Registering the same id again overwrites (the Close-removal still targets that id).
        var second = new Window();
        svc.RegisterWorkflowEditor("wf-1", second);
        Assert.Same(second, svc.WorkflowEditorWindows["wf-1"]);

        // Closing the registered window removes the entry (Closed-removal semantics).
        second.Show();
        second.Close();
        Assert.DoesNotContain("wf-1", svc.WorkflowEditorWindows);
    }
}
