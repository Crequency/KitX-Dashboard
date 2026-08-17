using System;
using System.Linq;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Models;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Bench UX v2 §4.4 structured parameter-injection map + §4.6 diagnostics + C17 comments.
/// Exercises the VM layer without the UI thread.
/// </summary>
public class BenchV2CanvasTests
{
    private static BenchCanvasViewModel Canvas(params ToolkitWorkflow[] workflows)
    {
        var toolkit = new Toolkit
        {
            Workflows = workflows.ToList(),
        };
        return new BenchCanvasViewModel(toolkit);
    }

    [Fact]
    public void StructuredParamRows_ParseAndPersist_AllThreeSources()
    {
        var binding = new TriggerBinding
        {
            Workflow = "wf1",
            Params =
            {
                ["text"] = "$payload.input",
                ["model"] = "$output.model",
                ["lang"] = "zh-CN",
            },
        };

        var rows = binding.Params.Select(kv => new BenchParamRowVM(binding, kv.Key, kv.Value, true, () => { })).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(BenchParamSourceKind.Payload, rows[0].SourceKind);
        Assert.Equal("input", rows[0].Path);
        Assert.Equal(BenchParamSourceKind.Output, rows[1].SourceKind);
        Assert.Equal("model", rows[1].Path);
        Assert.Equal(BenchParamSourceKind.Literal, rows[2].SourceKind);
        Assert.Equal("zh-CN", rows[2].LiteralValue);

        rows[0].Path = "payload.name";
        rows[1].Path = "result.score";
        rows[2].LiteralValue = "zh-TW";

        Assert.Equal("$payload.payload.name", binding.Params["text"]);
        Assert.Equal("$output.result.score", binding.Params["model"]);
        Assert.Equal("zh-TW", binding.Params["lang"]);
    }

    [Fact]
    public void StructuredParamRows_RejectInvalidPath_AndWarnForOutputOnSpawnBinding()
    {
        var binding = new TriggerBinding { Workflow = "wf1" };
        var output = new BenchParamRowVM(binding, "x", "$output.a", false, () => { });
        Assert.True(output.HasWarning);

        output.SourceKind = BenchParamSourceKind.Payload;
        output.Path = "a..b";
        Assert.NotNull(output.Error);

        output.Path = "ok.name";
        Assert.Null(output.Error);
        Assert.Equal("$payload.ok.name", binding.Params["x"]);
    }

    [Fact]
    public void CommentNode_AddEditDelete_StaysInConfig()
    {
        var canvas = Canvas();
        canvas.AddCommentCommand.Execute(null);

        var comment = Assert.Single(canvas.Config.Comments);
        var node = Assert.Single(canvas.Nodes.OfType<BenchNodeVM>().Where(n => n.IsComment));
        Assert.Equal(comment.Id, node.ConfigId);

        canvas.SelectedNode = node;
        canvas.SelectedCommentText = "hello comment";
        Assert.Equal("hello comment", canvas.Config.Comments[0].Text);
        Assert.Equal("hello comment", node.CommentText);

        canvas.DeleteNodeCommand.Execute(node);
        Assert.Empty(canvas.Config.Comments);
        Assert.DoesNotContain(canvas.Nodes.OfType<BenchNodeVM>(), n => n.IsComment);
    }

    [Fact]
    public void Diagnostics_ParseTriggerAndWorkflowTargets()
    {
        var toolkit = new Toolkit
        {
            Triggers = { new Trigger { Id = "trg1", Type = TriggerType.Manual, Bindings = { new TriggerBinding { Workflow = "missing" } } } },
        };
        var canvas = new BenchCanvasViewModel(toolkit);

        Assert.Contains(canvas.Diagnostics, d => d.NodeId == "missing" && d.NodeKind == "workflow");
    }

    [Fact]
    public void Rebuild_KeepsCommentNode_AndResetsOnlyMissingLocation()
    {
        var toolkit = new Toolkit
        {
            Comments = { new ToolkitComment { Id = "note", Text = "persisted text" } },
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        var node = Assert.Single(canvas.Nodes.OfType<BenchNodeVM>().Where(n => n.IsComment));

        canvas.Rebuild();

        var rebuilt = Assert.Single(canvas.Nodes.OfType<BenchNodeVM>().Where(n => n.IsComment));
        Assert.Equal(node.ConfigId, rebuilt.ConfigId);
        Assert.Equal(node.Location, rebuilt.Location); // in-memory rebuild preserves location; location is never in config
    }

    [Fact]
    public void AddFirstNode_RaisesHasContent()
    {
        var canvas = Canvas();
        var raised = false;
        canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BenchCanvasViewModel.HasContent))
                raised = true;
        };

        canvas.AddTriggerCommand.Execute("Manual");

        Assert.True(raised);
        Assert.True(canvas.HasContent);
        Assert.Single(canvas.Nodes.OfType<BenchNodeVM>());
    }

    [Fact]
    public void AddTimer_DefaultsToPeriodicInterval()
    {
        var canvas = Canvas();
        canvas.AddTriggerCommand.Execute("Timer");

        var timer = Assert.Single(canvas.Config.Triggers);
        Assert.Equal(TriggerType.Timer, timer.Type);
        Assert.Equal(1000, timer.Config.IntervalMs);
    }

    [Fact]
    public void CompletionEdge_RemoveBinding_UpdatesConfigAndCanvas()
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
                new Trigger
                {
                    Id = "comp",
                    Type = TriggerType.WorkflowCompletion,
                    Config = new TriggerConfig { From = "wf1" },
                    Bindings = { new TriggerBinding { Workflow = "wf2" } },
                },
            },
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        var edge = Assert.Single(canvas.Connections.OfType<BenchConnectionVM>());

        canvas.SelectConnectionCommand.Execute(edge);
        Assert.True(canvas.IsCompletionEdgeSelected);
        var row = Assert.Single(canvas.CompletionInspectorBindings);

        canvas.RemoveBindingCommand.Execute(row);

        Assert.Empty(canvas.Connections.OfType<BenchConnectionVM>());
        Assert.Empty(canvas.Config.Triggers); // last completion binding removed → trigger removed
        Assert.False(canvas.IsCompletionEdgeSelected);
    }

    [Fact]
    public void BindingRow_WorkflowChange_SyncsCanvasEdge()
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
                new Trigger
                {
                    Id = "trg1",
                    Type = TriggerType.Manual,
                    Config = new TriggerConfig { Surface = "auto" },
                    Bindings = { new TriggerBinding { Workflow = "wf1" } },
                },
            },
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        canvas.SelectedNode = canvas.Nodes.OfType<BenchNodeVM>().First(n => n.Kind == BenchCanvasViewModel.BenchNodeKind.Source);

        var row = Assert.Single(canvas.SelectedBindings);
        row.SelectedWorkflow = "wf2";

        Assert.Equal("wf2", toolkit.Triggers[0].Bindings[0].Workflow);
        Assert.Contains(canvas.Connections.OfType<BenchConnectionVM>(), c =>
            c.Source is BenchConnectorVM s && s.Key == "src:trg1"
            && c.Target is BenchConnectorVM t && t.Key == "in:wf2");
        Assert.DoesNotContain(canvas.Connections.OfType<BenchConnectionVM>(), c =>
            c.Target is BenchConnectorVM t && t.Key == "in:wf1");
    }

    [Fact]
    public void WorkflowRename_RestoresInspectorSelection()
    {
        var toolkit = new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" } },
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        canvas.SelectedNode = canvas.Nodes.OfType<BenchNodeVM>().Single();

        canvas.SelectedWorkflowId = "wf-renamed";

        Assert.Equal("wf-renamed", canvas.SelectedWorkflowId);
        Assert.NotNull(canvas.SelectedNode);
        Assert.Equal("wf-renamed", canvas.SelectedNode.ConfigId);
        Assert.True(canvas.IsWorkflowSelected);
    }

    [Fact]
    public void SelectingTrigger_RaisesInspectorFieldNotifications()
    {
        var toolkit = new Toolkit
        {
            Triggers =
            {
                new Trigger
                {
                    Id = "trg-plugin",
                    Type = TriggerType.PluginEvent,
                    Config = new TriggerConfig { PluginName = "My.Plugin", TriggerName = "on-data" },
                },
            },
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        var raised = new System.Collections.Generic.HashSet<string?>();
        canvas.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        canvas.SelectedNode = canvas.Nodes.OfType<BenchNodeVM>().Single();

        Assert.Equal("My.Plugin", canvas.SelectedTriggerPluginName);
        Assert.Equal("on-data", canvas.SelectedTriggerTriggerName);
        Assert.Contains(nameof(BenchCanvasViewModel.SelectedTriggerPluginName), raised);
        Assert.Contains(nameof(BenchCanvasViewModel.SelectedTriggerTriggerName), raised);
        Assert.Contains(nameof(BenchCanvasViewModel.SelectedTriggerSurface), raised);
    }

    [Fact]
    public void PanelControlEdit_RefreshesCanvasNodePreview()
    {
        var toolkit = new Toolkit
        {
            UiPanel = new UiPanel
            {
                Layout = "stack",
                Controls =
                {
                    new UiControl { Type = "Text", Id = "text_1", Text = "old text" },
                },
            },
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        canvas.SelectedNode = canvas.Nodes.OfType<BenchNodeVM>().Single(n => n.Kind == BenchCanvasViewModel.BenchNodeKind.Panel);

        var panelNode = canvas.Nodes.OfType<BenchNodeVM>().Single(n => n.Kind == BenchCanvasViewModel.BenchNodeKind.Panel);
        var preview = Assert.Single(panelNode.ControlVMs);
        Assert.Equal("old text", preview.Text);

        var inspectorRow = Assert.Single(canvas.SelectedControlVMs);
        inspectorRow.Text = "new text";

        Assert.Equal("new text", preview.Text);
        Assert.Equal("new text", panelNode.ControlVMs[0].Text);
    }

    [Fact]
    public void CompletionEdge_AddBinding_FromInspector()
    {
        var toolkit = new Toolkit
        {
            Workflows =
            {
                new ToolkitWorkflow { Id = "wf1", Name = "W1", File = "w1.kcs" },
                new ToolkitWorkflow { Id = "wf2", Name = "W2", File = "w2.kcs" },
                new ToolkitWorkflow { Id = "wf3", Name = "W3", File = "w3.kcs" },
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
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        var edge = Assert.Single(canvas.Connections.OfType<BenchConnectionVM>());
        canvas.SelectConnectionCommand.Execute(edge);
        canvas.AddWorkflowId = "wf3";

        canvas.AddBindingCommand.Execute("wf3");

        Assert.Equal(2, toolkit.Triggers.Single().Bindings.Count);
        Assert.Equal(2, canvas.Connections.OfType<BenchConnectionVM>().Count());
        Assert.Equal(2, canvas.CompletionInspectorBindings.Count);
        Assert.Contains(canvas.Connections.OfType<BenchConnectionVM>(), c =>
            c.Target is BenchConnectorVM t && t.Key == "in:wf3");
    }

    [Fact]
    public void BindingRow_WorkflowOptions_TrackWorkflowChanges()
    {
        var toolkit = new Toolkit
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
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        canvas.SelectedNode = canvas.Nodes.OfType<BenchNodeVM>().First(n => n.Kind == BenchCanvasViewModel.BenchNodeKind.Source);
        var row = Assert.Single(canvas.SelectedBindings);
        Assert.Contains("wf1", row.WorkflowOptions);

        toolkit.Workflows.Add(new ToolkitWorkflow { Id = "wf2", Name = "W2", File = "w2.kcs" });
        canvas.Rebuild();

        Assert.Contains("wf2", canvas.WorkflowIdOptions);
        Assert.Contains("wf2", row.WorkflowOptions);
    }

    [Fact]
    public void CycleDiagnostic_HasLocatableWorkflowTarget()
    {        var toolkit = new Toolkit
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
                new Trigger
                {
                    Id = "edge-ba",
                    Type = TriggerType.WorkflowCompletion,
                    Config = new TriggerConfig { From = "B" },
                    Bindings = { new TriggerBinding { Workflow = "A" } },
                },
            },
        };
        var canvas = new BenchCanvasViewModel(toolkit);

        var cycle = Assert.Single(canvas.Diagnostics.Where(d => d.Message.Contains("cycle detected")));
        Assert.NotNull(cycle.NodeId);
        Assert.True(cycle.CanLocate);
    }

    [Fact]
    public void ApplyWorkflowSaved_UpdatesMatchingNodeLabelAndModel()
    {
        var toolkit = new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "Old Name", File = "w1.kcs" } },
        };
        var canvas = new BenchCanvasViewModel(toolkit);
        var node = Assert.Single(canvas.Nodes.OfType<BenchNodeVM>());
        Assert.Equal("Old Name", node.Title);

        canvas.ApplyWorkflowSaved("wf1", "New Name", "a description");

        Assert.Equal("New Name", toolkit.Workflows[0].Name);
        // Rebuild re-projects the canvas, so re-query the (new) node for its label.
        Assert.Equal("New Name", Assert.Single(canvas.Nodes.OfType<BenchNodeVM>()).Title);
    }

    [Fact]
    public void ApplyWorkflowSaved_UnknownId_IsNoOp()
    {
        var toolkit = new Toolkit
        {
            Workflows = { new ToolkitWorkflow { Id = "wf1", Name = "Keep Me", File = "w1.kcs" } },
        };
        var canvas = new BenchCanvasViewModel(toolkit);

        canvas.ApplyWorkflowSaved("does-not-exist", "Should Not Apply", null);

        Assert.Equal("Keep Me", toolkit.Workflows[0].Name);
        Assert.Equal("Keep Me", Assert.Single(canvas.Nodes.OfType<BenchNodeVM>()).Title);
    }
}
