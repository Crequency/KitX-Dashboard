using System.Linq;
using Avalonia;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Models;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Pins the Bench "config is the single source of truth, canvas is a live editor" closed
/// loop (GUI RFC §8.3): inspector edits write through to the config model AND refresh the
/// canvas projection, binding rows stay in sync with canvas edges, and Rebuild preserves
/// node locations. These are plain xunit tests (no headless UI needed) — they exercise the
/// VM layer directly against a real <see cref="Toolkit"/>.
/// </summary>
public class BenchClosedLoopTests
{
    private static BenchNodeVM SourceNode(BenchCanvasViewModel canvas, string id)
        => canvas.Nodes.OfType<BenchNodeVM>().First(n => n.ConfigId == id);

    private static BenchNodeVM WorkflowNode(BenchCanvasViewModel canvas, string id)
        => canvas.Nodes.OfType<BenchNodeVM>().First(n => n.ConfigId == id);

    [Fact]
    public void WorkflowId_Change_Cascades_To_Bindings_Completion_And_Node_ConfigId()
    {
        var toolkit = new Toolkit
        {
            Workflows =
            {
                new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" },
                new ToolkitWorkflow { Id = "wf2", Name = "W2", File = "w2.kcs" },
            },
            Triggers =
            {
                new Trigger { Id = "trg1", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } },
                new Trigger { Id = "trg2", Type = TriggerType.WorkflowCompletion, Config = new TriggerConfig { From = "wf1" } },
            },
        };
        toolkit.Triggers[0].Bindings.Add(new TriggerBinding { Workflow = "wf1" });
        toolkit.Triggers[1].Bindings.Add(new TriggerBinding { Workflow = "wf2" });

        var canvas = new BenchCanvasViewModel(toolkit);
        var edgeCount = canvas.Connections.Count;

        canvas.SelectedNode = WorkflowNode(canvas, "wf1");
        canvas.SelectedWorkflowId = "wf1-renamed";

        Assert.Equal("wf1-renamed", toolkit.Workflows[0].Id);
        Assert.Equal("wf1-renamed", toolkit.Triggers[0].Bindings[0].Workflow);
        Assert.Equal("wf1-renamed", toolkit.Triggers[1].Config.From);
        Assert.Equal("wf1-renamed", WorkflowNode(canvas, "wf1-renamed").ConfigId);
        Assert.Equal(edgeCount, canvas.Connections.Count); // edges survive the cascade
    }

    [Fact]
    public void AddBinding_Adds_Edge_And_RemoveBinding_Removes_It()
    {
        var toolkit = new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" } },
            Triggers = { new Trigger { Id = "trg1", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } } },
        };

        var canvas = new BenchCanvasViewModel(toolkit);
        canvas.SelectedNode = SourceNode(canvas, "trg1");

        canvas.AddBindingCommand.Execute("wf1");

        var edge = canvas.Connections.OfType<BenchConnectionVM>()
            .FirstOrDefault(c => c.Source is BenchConnectorVM s && s.Key == "src:trg1"
                              && c.Target is BenchConnectorVM t && t.Key == "in:wf1");
        Assert.NotNull(edge);
        Assert.Single(canvas.SelectedBindings);

        var row = canvas.SelectedBindings.First();
        canvas.RemoveBindingCommand.Execute(row);

        Assert.DoesNotContain(canvas.Connections.OfType<BenchConnectionVM>(), c =>
            c.Source is BenchConnectorVM s && s.Key == "src:trg1"
            && c.Target is BenchConnectorVM t && t.Key == "in:wf1");
        Assert.Empty(canvas.SelectedBindings);
    }

    [Fact]
    public void PluginName_Change_Refreshes_Source_KindLabel()
    {
        var toolkit = new Toolkit
        {
            Triggers = { new Trigger { Id = "trg1", Type = TriggerType.PluginEvent, Config = new TriggerConfig { PluginName = "old" } } },
        };

        var canvas = new BenchCanvasViewModel(toolkit);
        var node = SourceNode(canvas, "trg1");
        canvas.SelectedNode = node;

        canvas.SelectedTriggerPluginName = "myplugin";

        Assert.Equal("插件: myplugin", node.KindLabel);
    }

    [Fact]
    public void Rebuild_Preserves_Node_Location()
    {
        var toolkit = new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" } },
        };

        var canvas = new BenchCanvasViewModel(toolkit);
        var node = WorkflowNode(canvas, "wf1");
        node.Location = new Point(123, 456);

        canvas.Rebuild();

        Assert.Equal(new Point(123, 456), WorkflowNode(canvas, "wf1").Location);
    }

    [Fact]
    public void TriggerId_Change_Refreshes_Node_Title()
    {
        var toolkit = new Toolkit
        {
            Triggers = { new Trigger { Id = "trg1", Type = TriggerType.Manual, Config = new TriggerConfig { Surface = "auto" } } },
        };

        var canvas = new BenchCanvasViewModel(toolkit);
        canvas.SelectedNode = SourceNode(canvas, "trg1");

        canvas.SelectedTriggerId = "newid";

        // The rename rebuilds the graph (so the pin Key src:{id} stays consistent) —
        // assert on the rebuilt node, and that the selection was restored.
        var rebuilt = Assert.IsType<BenchNodeVM>(Assert.Single(canvas.Nodes));
        Assert.Equal("newid", rebuilt.Title);
        Assert.Equal("newid", rebuilt.ConfigId);
        Assert.Equal(rebuilt, canvas.SelectedNode);
    }
}
