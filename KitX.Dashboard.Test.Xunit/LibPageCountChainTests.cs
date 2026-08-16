// ─────────────────────────────────────────────────────────────────────────────
// Regression tests for the "库 page shows 0 plugins connected" report:
//   1. LibPageViewModel's count/list must follow UIStateService.PluginInfos
//      changes posted from the server thread (AppViewModel's marshalled Add).
//   2. A disposed-and-recreated page (navigation away and back) must resubscribe —
//      Dispose() unsubscribes on Unloaded, so a reused page instance would be
//      frozen forever at its first-render values.
// ─────────────────────────────────────────────────────────────────────────────

using Avalonia.Headless.XUnit;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels.Pages;
using KitX.Shared.CSharp.Plugin;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

public class LibPageCountChainTests
{
    private static PluginInfo MakeInfo(string name) => new()
    {
        Name = name,
        AuthorName = "t",
        Version = "0.1.0",
    };

    [AvaloniaFact]
    public void Count_And_List_Follow_Live_Connections()
    {
        UIStateService.PluginInfos.Clear();
        var vm = new LibPageViewModel();

        Assert.Equal("0", vm.PluginsCount);
        Assert.Empty(vm.DisplayedPluginInfos);

        // Server-thread shape: AppViewModel posts the mutation to the UI thread.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.LLM")));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("1", vm.PluginsCount);
        Assert.Single(vm.DisplayedPluginInfos);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.Context"));
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.FileTools"));
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("3", vm.PluginsCount);
        Assert.Equal(3, vm.DisplayedPluginInfos.Count);

        vm.Dispose();
    }

    [AvaloniaFact]
    public void Fresh_Page_Instance_After_Dispose_Shows_Current_Count()
    {
        // Navigation away disposes the VM's subscription; navigating back must not
        // show a stale 0 — a fresh instance reads the live count in its constructor.
        UIStateService.PluginInfos.Clear();
        var first = new LibPageViewModel();
        first.Dispose(); // navigated away while 0 connected

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.LLM")));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var second = new LibPageViewModel();
        Assert.Equal("1", second.PluginsCount);
        Assert.Single(second.DisplayedPluginInfos);
        second.Dispose();
    }
}
