using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using KitX.ToolKit.Models;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Read-only projection of a <see cref="Toolkit"/> config onto a NodifyM Bench canvas
/// (config is the truth; the canvas is a projection). Spawn triggers become source nodes on
/// the left; workflows become nodes on the right; Spawn bindings and WorkflowCompletion
/// edges become connections. Edits to the config would rebuild the graph.
/// </summary>
public sealed class BenchCanvasViewModel : NodifyEditorViewModelBase
{
    public BenchCanvasViewModel(Toolkit toolkit)
    {
        ArgumentNullException.ThrowIfNull(toolkit);
        BuildGraph(toolkit);
    }

    /// <summary>True when the config had any nodes.</summary>
    public bool HasContent => Nodes.Count > 0;

    private void BuildGraph(Toolkit toolkit)
    {
        Nodes.Clear();
        Connections.Clear();

        // Key → connector: source nodes keyed "src:{id}", workflow inputs "in:{id}", outputs "out:{id}".
        var pins = new Dictionary<string, BenchConnectorVM>(StringComparer.Ordinal);
        const double sourceX = 60;
        const double workflowX = 420;
        double y = 40;

        // Source nodes (Spawn triggers) — one output pin each.
        foreach (var trigger in toolkit.Triggers.Where(t => t.Type != TriggerType.WorkflowCompletion))
        {
            var node = new BenchNodeVM(trigger.Id, "Source", SourceKindLabel(trigger), new Point(sourceX, y));
            var outPin = new BenchConnectorVM("触发", ConnectorViewModelBase.ConnectorFlow.Output);
            node.Output.Add(outPin);
            pins["src:" + trigger.Id] = outPin;
            Nodes.Add(node);
            y += 90;
        }

        // Workflow nodes — one input + one output pin each.
        y = 40;
        foreach (var wf in toolkit.Workflows)
        {
            var node = new BenchNodeVM(wf.Name, "Workflow", "工作流", new Point(workflowX, y));
            var inPin = new BenchConnectorVM("输入", ConnectorViewModelBase.ConnectorFlow.Input);
            var outPin = new BenchConnectorVM("输出", ConnectorViewModelBase.ConnectorFlow.Output);
            node.Input.Add(inPin);
            node.Output.Add(outPin);
            pins["in:" + wf.Id] = inPin;
            pins["out:" + wf.Id] = outPin;
            Nodes.Add(node);
            y += 90;
        }

        // Connections: Spawn trigger → binding workflow; workflow → workflow (completion).
        foreach (var trigger in toolkit.Triggers)
        {
            if (trigger.Type == TriggerType.WorkflowCompletion)
            {
                if (string.IsNullOrWhiteSpace(trigger.Config?.From))
                    continue;
                foreach (var binding in trigger.Bindings)
                    AddConnection(pins, "out:" + trigger.Config.From, "in:" + binding.Workflow);
            }
            else
            {
                foreach (var binding in trigger.Bindings)
                    AddConnection(pins, "src:" + trigger.Id, "in:" + binding.Workflow);
            }
        }
    }

    private void AddConnection(Dictionary<string, BenchConnectorVM> pins, string sourceKey, string targetKey)
    {
        if (pins.TryGetValue(sourceKey, out var source) && pins.TryGetValue(targetKey, out var target))
            Connections.Add(new BenchConnectionVM(this, source, target));
    }

    private static string SourceKindLabel(Trigger trigger) => trigger.Type switch
    {
        TriggerType.Manual => "手动",
        TriggerType.PluginEvent => $"插件: {trigger.Config?.PluginName}",
        TriggerType.UIEvent => $"UI: {trigger.Config?.Control}",
        TriggerType.Timer => "定时",
        _ => trigger.Type.ToString(),
    };
}
