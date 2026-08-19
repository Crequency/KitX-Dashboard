// ─────────────────────────────────────────────────────────────────────────────
// G4 regression tests for the plugin-library page filter:
//   1. A startup storm of many connection events is coalesced into one incremental
//      reconciliation (no 300 x O(300) Clear+rebuild) and yields a correct list.
//   2. The rendered window (virtualization fallback: Avalonia 11.3 has no
//      virtualizing wrap/grid panel) grows via "load more".
//   3. The keyword path still does a full recompute (the only full path).
//   4. Live Add/Remove sync the realized window incrementally.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels.Pages;
using KitX.Shared.CSharp.Plugin;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

public class LibPageIncrementalTests
{
    // Mirrors LibPageViewModel.InitialBatchSize — the initial realized window.
    private const int InitialBatch = 150;

    private static PluginInfo MakeInfo(string name) => new()
    {
        Name = name,
        AuthorName = "t",
        Version = "0.1.0",
    };

    [AvaloniaFact]
    public void Storm_Of_Adds_Coalesces_Into_Correct_Window()
    {
        UIStateService.PluginInfos.Clear();
        var vm = new LibPageViewModel();

        // 300 plugins connected in one burst (server-thread shape, marshalled to UI thread).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            for (var i = 0; i < 300; i++)
                UIStateService.PluginInfos.Add(MakeInfo($"KitX.Agent.{i:D3}"));
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("300", vm.PluginsCount);
        Assert.Equal(InitialBatch, vm.DisplayedPluginInfos.Count);
        Assert.True(vm.HasMoreItems);

        // Incremental result must match a full recompute (empty keyword => all match):
        // the realized window is the first InitialBatch source plugins in order.
        var expected = UIStateService.PluginInfos.Take(InitialBatch).Select(p => p.Name).ToArray();
        Assert.Equal(expected, vm.DisplayedPluginInfos.Select(p => p.Name).ToArray());

        vm.Dispose();
    }

    [AvaloniaFact]
    public void Load_More_Grows_The_Realized_Window()
    {
        UIStateService.PluginInfos.Clear();
        var vm = new LibPageViewModel();

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            for (var i = 0; i < 300; i++)
                UIStateService.PluginInfos.Add(MakeInfo($"KitX.Agent.{i:D3}"));
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.LoadMoreCommand!.Execute().Subscribe();
        Assert.Equal(300, vm.DisplayedPluginInfos.Count);
        Assert.False(vm.HasMoreItems);

        vm.Dispose();
    }

    [AvaloniaFact]
    public void Keyword_Change_Restricts_The_Full_View()
    {
        UIStateService.PluginInfos.Clear();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.LLM"));
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.Context"));
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.FileTools"));
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var vm = new LibPageViewModel();
        Assert.Equal(3, vm.DisplayedPluginInfos.Count);

        vm.SearchingText = "LLM";
        Assert.Single(vm.DisplayedPluginInfos);
        Assert.Equal("KitX.Agent.LLM", vm.DisplayedPluginInfos[0].Name);

        vm.SearchingText = "";
        Assert.Equal(3, vm.DisplayedPluginInfos.Count);

        vm.Dispose();
    }

    [AvaloniaFact]
    public void Live_Remove_Syncs_The_Window_Incrementally()
    {
        UIStateService.PluginInfos.Clear();
        var vm = new LibPageViewModel();

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.LLM"));
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.Context"));
            UIStateService.PluginInfos.Add(MakeInfo("KitX.Agent.FileTools"));
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, vm.DisplayedPluginInfos.Count);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            UIStateService.PluginInfos.RemoveAt(0));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.DisplayedPluginInfos.Count);
        Assert.Equal("KitX.Agent.Context", vm.DisplayedPluginInfos[0].Name);

        vm.Dispose();
    }
}
