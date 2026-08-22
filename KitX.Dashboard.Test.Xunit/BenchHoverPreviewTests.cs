using System.Linq;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Models;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// RFC C14 hover-preview validation: when a dragged connection hovers an illegal target pin,
/// that pin turns red (CanConnect=false, EffectiveBorderColorHex="#F44336"); a legal target
/// stays green; clearing the preview resets every pin. Exercises the VM layer without a UI thread.
/// </summary>
public class BenchHoverPreviewTests
{
    private const string RedHex = "#F44336";

    private static BenchConnectorVM Connector(BenchCanvasViewModel canvas, string key)
        => canvas.Nodes.OfType<BenchNodeVM>()
            .SelectMany(n => n.Input.Cast<BenchConnectorVM>().Concat(n.Output.Cast<BenchConnectorVM>()))
            .First(c => c.Key == key);

    /// <summary>Simulates dragging a connection out of <paramref name="source"/> while
    /// hovering <paramref name="target"/> (mutates PendingConnection state only).</summary>
    private static void Hover(BenchCanvasViewModel canvas, BenchConnectorVM source, BenchConnectorVM target)
    {
        canvas.PendingConnection.Source = source;
        canvas.PendingConnection.PreviewTarget = target;
    }

    [Fact]
    public void HoverIllegal_NonWorkflowTarget_FlaggedRed()
    {
        // Two triggers; hovering a trigger's out pin (not a workflow) while binding is illegal.
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Triggers =
            {
                new Trigger { Id = "trg1", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } },
                new Trigger { Id = "trg2", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } },
            },
        });

        var src = Connector(canvas, "src:trg1");
        var badTarget = Connector(canvas, "src:trg2");

        Hover(canvas, src, badTarget);

        Assert.False(badTarget.CanConnect);
        Assert.Equal(RedHex, badTarget.EffectiveBorderColorHex);
    }

    [Fact]
    public void HoverIllegal_SelfConnection_FlaggedRed()
    {
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Triggers = { new Trigger { Id = "trg1", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } } },
        });

        var src = Connector(canvas, "src:trg1");
        Hover(canvas, src, src);

        Assert.False(src.CanConnect);
        Assert.Equal(RedHex, src.EffectiveBorderColorHex);
    }

    [Fact]
    public void HoverIllegal_DuplicateBinding_FlaggedRed()
    {
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" } },
            Triggers =
            {
                new Trigger
                {
                    Id = "trg1",
                    Type = TriggerType.Manual,
                    Config = new TriggerConfig { Surface = "auto" },
                    Bindings = { new TriggerBinding { Workflow = "wf1" } },
                },
            },
        });

        var src = Connector(canvas, "src:trg1");
        var wfIn = Connector(canvas, "in:wf1");

        Hover(canvas, src, wfIn);

        Assert.False(wfIn.CanConnect);
        Assert.Equal(RedHex, wfIn.EffectiveBorderColorHex);
    }

    [Fact]
    public void HoverIllegal_CompletionToNonWorkflow_FlaggedRed()
    {
        // A workflow completion edge may only target a workflow; hovering a trigger out pin is illegal.
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" } },
            Triggers = { new Trigger { Id = "trg1", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } } },
        });

        var srcOut = Connector(canvas, "out:wf1");
        var badTarget = Connector(canvas, "src:trg1");

        Hover(canvas, srcOut, badTarget);

        Assert.False(badTarget.CanConnect);
        Assert.Equal(RedHex, badTarget.EffectiveBorderColorHex);
    }

    [Fact]
    public void HoverIllegal_DuplicateCompletionEdge_FlaggedRed()
    {
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Workflows =
            {
                new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" },
                new ToolkitWorkflow { Id = "wf2", Name = "W2", File = "w2.kcs" },
            },
            Triggers =
            {
                new Trigger
                {
                    Id = "comp",
                    Type = TriggerType.WorkflowCompletion,
                    Config = new TriggerConfig { From = "wf1" },
                    Bindings = { new TriggerBinding { Workflow = "wf2" } },
                },
            },
        });

        var srcOut = Connector(canvas, "out:wf1");
        var wf2In = Connector(canvas, "in:wf2");

        Hover(canvas, srcOut, wf2In);

        Assert.False(wf2In.CanConnect);
        Assert.Equal(RedHex, wf2In.EffectiveBorderColorHex);
    }

    [Fact]
    public void HoverIllegal_CompletionCycle_FlaggedRed()
    {
        // Existing completion edge A→B; hovering B's out over A's in would close a cycle.
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Workflows =
            {
                new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" },
                new ToolkitWorkflow { Id = "B", Name = "B", File = "b.kcs" },
            },
            Triggers =
            {
                new Trigger
                {
                    Id = "edge-ab",
                    Type = TriggerType.WorkflowCompletion,
                    Config = new TriggerConfig { From = "A" },
                    Bindings = { new TriggerBinding { Workflow = "B" } },
                },
            },
        });

        var bOut = Connector(canvas, "out:B");
        var aIn = Connector(canvas, "in:A");

        Hover(canvas, bOut, aIn);

        Assert.False(aIn.CanConnect);
        Assert.Equal(RedHex, aIn.EffectiveBorderColorHex);
    }

    [Fact]
    public void HoverLegal_BindingTarget_StaysNormal()
    {
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" } },
            Triggers = { new Trigger { Id = "trg1", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } } },
        });

        var src = Connector(canvas, "src:trg1");
        var wfIn = Connector(canvas, "in:wf1");

        Hover(canvas, src, wfIn);

        Assert.True(wfIn.CanConnect);
        Assert.NotEqual(RedHex, wfIn.EffectiveBorderColorHex);
    }

    [Fact]
    public void HoverLegal_CompletionEdge_StaysNormal()
    {
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Workflows =
            {
                new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" },
                new ToolkitWorkflow { Id = "wf2", Name = "W2", File = "w2.kcs" },
            },
        });

        var wf1Out = Connector(canvas, "out:wf1");
        var wf2In = Connector(canvas, "in:wf2");

        Hover(canvas, wf1Out, wf2In);

        Assert.True(wf2In.CanConnect);
        Assert.NotEqual(RedHex, wf2In.EffectiveBorderColorHex);
    }

    [Fact]
    public void PreviewTargetCleared_ResetsAllPins()
    {
        var canvas = new BenchCanvasViewModel(new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" } },
            Triggers = { new Trigger { Id = "trg1", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } } },
        });

        var src = Connector(canvas, "src:trg1");
        var wfIn = Connector(canvas, "in:wf1");
        Hover(canvas, src, wfIn); // legal hover first — no pin flagged

        // Hover an illegal self-target, then clear the preview.
        Hover(canvas, src, src);
        Assert.False(src.CanConnect);

        canvas.PendingConnection.PreviewTarget = null;

        Assert.True(src.CanConnect);
        Assert.True(wfIn.CanConnect);
        Assert.NotEqual(RedHex, src.EffectiveBorderColorHex);
        Assert.NotEqual(RedHex, wfIn.EffectiveBorderColorHex);
    }
}
