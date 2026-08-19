using System.Linq;
using KitX.Dashboard.ViewModels;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Pins the B1-refactored Helper-Function parameter collection operations. These were
/// moved out of WorkflowEditorWindowV6's code-behind (OnAddParameter / the remove branch
/// of OnConstantsButtonClick) into VM RelayCommands (<c>AddParameterCommand</c> /
/// <c>RemoveParameterCommand</c>) bound directly in XAML. These are plain xunit tests (no
/// headless UI needed) — they exercise the command layer against a real VM, asserting the
/// new commands mirror the old code-behind behaviour: each op touches BOTH the panel's
/// ObservableCollection AND the selected helper's Parameters list.
/// </summary>
public class WorkflowEditorV6ParameterTests
{
    private static WorkflowEditorViewModelV6 CreateVm()
    {
        var registry = new BuiltinFunctionRegistry();
        // Backend lenses are required by the ctor; the 6 service interfaces are unused by
        // the parameter commands, so null is safe here.
        return new WorkflowEditorViewModelV6(
            new KsTextLens(registry),
            new BpGraphLens(registry),
            registry,
            pluginServer: null!,
            runner: null!,
            eventService: null!,
            configService: null!,
            fileStore: null!);
    }

    [Fact]
    public void AddParameter_Adds_To_Helper_And_Panel()
    {
        var vm = CreateVm();
        vm.SelectedHelperFunction = vm.HelperFunctions.First();
        var before = vm.SelectedHelperFunction!.Parameters.Count;

        vm.AddParameterCommand.Execute(null);

        Assert.Equal(before + 1, vm.SelectedHelperFunction.Parameters.Count);
        Assert.Equal(before + 1, vm.Parameters.Count);
        // Name follows the code-behind convention: param{N} where N is the new count.
        Assert.Equal($"param{before + 1}", vm.SelectedHelperFunction.Parameters[^1].Name);
    }

    [Fact]
    public void RemoveParameter_Removes_From_Helper_And_Panel()
    {
        var vm = CreateVm();
        vm.SelectedHelperFunction = vm.HelperFunctions.First();
        vm.AddParameterCommand.Execute(null);
        var param = vm.Parameters.Last();

        vm.RemoveParameterCommand.Execute(param);

        Assert.DoesNotContain(param, vm.SelectedHelperFunction.Parameters);
        Assert.DoesNotContain(param, vm.Parameters);
    }
}
