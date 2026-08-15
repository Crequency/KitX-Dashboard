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
}
