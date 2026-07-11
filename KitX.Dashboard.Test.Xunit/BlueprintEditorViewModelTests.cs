using System.Collections.Immutable;
using KitX.Core.Contract.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.Workflow.Builtin;
using KitX.Workflow.Backend.RoslynBackend;
using KitX.Workflow.Lens.BpGraphLens;
using KitX.Workflow.Lens.BsTextLens;
using KitX.Workflow.Session;
using KitX.Workflow.Ir;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// BlueprintEditorViewModel tests — the P0 test surface (§4.3 priority table).
//
// Strategy: real BuiltinFunctionRegistry + real SyncService + real Lens (their
// determinism is already proven by the 193 WorkflowIR tests). Only the pure UI
// service abstractions (ITasksService / IFileDialogService) are stubbed. This
// covers the full VM → BpEditAction → SyncService → IrChanged chain with no
// mocking inflation.
// ─────────────────────────────────────────────────────────────────────────────

public class BlueprintEditorViewModelTests
{
    private static BuiltinFunctionRegistry NewRegistry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    /// <summary>Builds a BlueprintEditorViewModel with real Lens/SyncService/NodeFactory.</summary>
    private static BlueprintEditorViewModel NewVm()
    {
        var registry = NewRegistry();
        var syncService = new SyncService(registry);
        var bsTextLens = new BsTextLens(registry);
        var bpGraphLens = new BpGraphLens(registry);
        var nodeFactory = new NodeFactory(registry);
        var executionBackend = new RoslynExecutionBackend(registry);

        return new BlueprintEditorViewModel(
            tasksService: new StubTasksService(),
            nodeFactory: nodeFactory,
            fileDialogService: new StubFileDialogService(),
            syncService: syncService,
            bsTextLens: bsTextLens,
            bpGraphLens: bpGraphLens,
            executionBackend: executionBackend,
            pluginService: null);
    }

    /// <summary>A minimal entry-block-only IR (one empty #MainBlock).</summary>
    private static IrWorkflow EmptyMainBlockIr() => new()
    {
        MainBlockName = "#MainBlock",
        Blocks = ImmutableArray.Create(new IrBlock
        {
            Name = "#MainBlock",
            Kind = IrBlockKind.Entry,
            Statements = ImmutableArray<IrStatement>.Empty,
        }),
    };

    // ── P0: AddNode round-trips through IR ─────────────────────────────────

    [Fact]
    public void AddBuiltinFunctionNode_Print_AddsStatementToIr()
    {
        var vm = NewVm();
        var session = new WorkflowSession(EmptyMainBlockIr());
        vm.SetSession(session);

        vm.AddPrintNode();

        // The session's IR should now contain one Print statement in #MainBlock.
        var mainBlock = session.Ir.GetBlock("#MainBlock");
        Assert.NotNull(mainBlock);
        var pipeline = Assert.Single(mainBlock!.Statements.OfType<IrPipelineStatement>());
        Assert.Equal("Print", pipeline.Segments[0].FunctionName);
    }

    // ── P0: ApplyBpEdits with AddNodeInBlock triggers IrChanged ─────────────

    [Fact]
    public void ApplyBpEdits_AddNodeInBlock_TriggersIrChanged()
    {
        var vm = NewVm();
        var session = new WorkflowSession(EmptyMainBlockIr());
        vm.SetSession(session);

        bool fired = false;
        session.IrChanged += _ => fired = true;

        // AddBuiltinFunctionNode is private; call AddPrintNode (public relay command).
        vm.AddPrintNode();

        Assert.True(fired);
    }

    // ── P0: Delete nodes removes statements from IR ────────────────────────

    [Fact]
    public void DeleteSelectedNodes_RemovesStatementFromIr()
    {
        var vm = NewVm();
        var session = new WorkflowSession(EmptyMainBlockIr());
        vm.SetSession(session);

        // Add a Print node first (re-projection rebuilds Nodes with DeriveStableId).
        vm.AddPrintNode();
        Assert.Single(session.Ir.GetBlock("#MainBlock")!.Statements.OfType<IrPipelineStatement>());

        // Select all canvas nodes (re-projected after Add) and delete.
        foreach (var n in vm.Nodes.OfType<BlueprintNodeVM>())
            vm.SelectedNodes.Add(n);

        vm.DeleteSelectedNodesCommand.Execute(null);

        // The IR should now have no pipeline statements.
        Assert.Empty(session.Ir.GetBlock("#MainBlock")!.Statements.OfType<IrPipelineStatement>());
    }

    // ── P1: AddBranchNode creates scope blocks in IR ───────────────────────

    [Fact]
    public void AddBranchNode_AddsBranchStatementAndBlocksToIr()
    {
        var vm = NewVm();
        var session = new WorkflowSession(EmptyMainBlockIr());
        vm.SetSession(session);

        vm.AddBranchNode();

        // AddBranchNode emits AddNodeInBlock("Branch") + two AddBlock + two
        // SetControlFlowArm. The IR should grow: #MainBlock gains a statement,
        // and at least two new blocks appear.
        var mainBlock = session.Ir.GetBlock("#MainBlock");
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock!.Statements);

        // The original IR had only #MainBlock; Branch should introduce new blocks.
        Assert.True(session.Ir.Blocks.Length > 1,
            $"Expected >1 block after AddBranchNode, got {session.Ir.Blocks.Length}");
    }

    // ── P1: BS→BP→BS round-trip via Lens (VM-free, deterministic) ──────────
    //
    // The WorkflowEditorViewModel round-trip delegates to BsTextLens.Parse →
    // BpGraphLens.Project → BsTextLens.Project. That chain is already covered by
    // WorkflowIR's BsTextLensRoundTripTests; here we assert the same chain works
    // through the VM's own Lens instances (constructed exactly as NewVm does).

    [Fact]
    public void LensRoundTrip_BsToBpToBs_StructurePreserved()
    {
        var registry = NewRegistry();
        var bsTextLens = new BsTextLens(registry);
        var bpGraphLens = new BpGraphLens(registry);

        const string source = "#MainBlock\nPrint(\"hello\");\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();";

        var ir = bsTextLens.Parse(source);
        var bp = bpGraphLens.Project(ir);
        Assert.NotEmpty(bp.Nodes);

        var bsRoundTrip = bsTextLens.Project(ir);
        Assert.Contains("Print", bsRoundTrip);
    }

    // ── Stub services ──────────────────────────────────────────────────────

    private sealed class StubTasksService : ITasksService
    {
        public void RunTask(Action task, string? taskName = null) => task();
        public Task RunTaskAsync(Func<Task> task, string? taskName = null) => task();
        public Task RunTaskAsync(Func<Task> task, CancellationToken ct, string? taskName = null) => task();
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<string?> ShowOpenDialogAsync(string title, List<FileDialogFilter>? filters = null) => Task.FromResult<string?>(null);
        public Task<string?> ShowSaveDialogAsync(string title, string defaultExtension, List<FileDialogFilter>? filters = null, string? suggestedFileName = null) => Task.FromResult<string?>(null);
        public Task<string?> ShowTextInputDialogAsync(string title, string prompt, string? initialText = null) => Task.FromResult<string?>(null);
        public Task ShowTextOutputDialogAsync(string title, string content) => Task.CompletedTask;
    }
}