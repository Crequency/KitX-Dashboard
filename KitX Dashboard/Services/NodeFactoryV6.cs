using System;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;

namespace KitX.Dashboard.Services;

// ─────────────────────────────────────────────────────────────────────────────
// NodeFactoryV6 — creates Contract BlueprintNodes for the v6 BP editor palette.
//
// Control-flow nodes (Branch/Each/While/Switch/break/continue) are hardcoded per
// KScript-Blueprint-Correspondence.md Appendix A — they are IR primitives, NOT
// registered in BuiltinFunctionRegistry. Builtin function nodes are driven by the
// PortSpec exposed by IBuiltinFunction via the registry.
//
// Newly created node IDs use a random n_XXXXXXXX format (distinct from the FNV-1a
// path-based IDs produced by BpRenderer). When the edited blueprint is reversed
// back to IR (P3-γ), BpReverseTranslator rebuilds the IR and IDs are recomputed.
// ─────────────────────────────────────────────────────────────────────────────

public static class NodeFactoryV6
{
    /// <summary>Creates a BuiltinFunctionNode with Exec + data pins from the registry's PortSpec.</summary>
    public static BuiltinFunctionNode CreateBuiltinFunctionNode(
        string functionName, BuiltinFunctionRegistry registry)
    {
        var builtin = registry.Get(functionName)
            ?? throw new ArgumentException(
                $"Builtin function '{functionName}' not registered.", nameof(functionName));

        var node = new BuiltinFunctionNode
        {
            Id = NewId(),
            Name = functionName,
            FunctionName = functionName,
            NodeType = BlueprintNodeType.BuiltinFunction,
        };

        // Exec pins — every non-definition usage node has Exec in/out.
        node.InputPins.Add(MakePin("Exec", PinDirection.Input, PinType.Execution));
        node.OutputPins.Add(MakePin("Exec", PinDirection.Output, PinType.Execution));

        // Data pins from the builtin's PortSpec (Op/From/To/Step/Value/Result/...).
        foreach (var port in builtin.InputPorts)
            node.InputPins.Add(MakePin(port.Name, PinDirection.Input, port.Type));
        foreach (var port in builtin.OutputPorts)
            node.OutputPins.Add(MakePin(port.Name, PinDirection.Output, port.Type));

        return node;
    }

    /// <summary>
    /// Creates a PluginTriggerNode — an alternative entry point with 0 input pins and
    /// 1 Exec output pin (same shape as EntryNode). Carries PluginName/TriggerName
    /// metadata; the frontend swaps it with the EntryNode when TriggerType=PluginEvent.
    /// </summary>
    public static PluginTriggerNode CreatePluginTriggerNode(string pluginName, string triggerName) => new()
    {
        Id = NewId(),
        Name = "PluginTrigger",
        X = 0,
        Y = 0,
        PluginName = pluginName,
        TriggerName = triggerName,
    };

    /// <summary>Creates a control-flow node with the pin layout defined in the correspondence doc.</summary>
    public static BuiltinFunctionNode CreateControlFlowNode(string functionName) => functionName switch    {
        "Branch" => CreateBranch(),
        "Each" => CreateEach(),
        "While" => CreateWhile(),
        "Switch" => CreateSwitch(),
        "break" => CreateTerminator("break"),
        "continue" => CreateTerminator("continue"),
        _ => throw new ArgumentException(
            $"Unknown control-flow node: {functionName}", nameof(functionName)),
    };

    // ── Control-flow pin layouts (Appendix A) ──

    private static BuiltinFunctionNode CreateBranch()
    {
        var n = MakeFunctionNode("Branch");
        n.InputPins.Add(MakePin("Exec", PinDirection.Input, PinType.Execution));
        n.InputPins.Add(MakePin("Condition", PinDirection.Input, PinType.Boolean));
        n.OutputPins.Add(MakePin("True", PinDirection.Output, PinType.Execution));
        n.OutputPins.Add(MakePin("False", PinDirection.Output, PinType.Execution));
        n.OutputPins.Add(MakePin("End", PinDirection.Output, PinType.Execution));
        return n;
    }

    private static BuiltinFunctionNode CreateEach()
    {
        var n = MakeFunctionNode("Each");
        n.InputPins.Add(MakePin("Exec", PinDirection.Input, PinType.Execution));
        n.InputPins.Add(MakePin("List", PinDirection.Input, PinType.Any));
        n.OutputPins.Add(MakePin("Body", PinDirection.Output, PinType.Execution));
        n.OutputPins.Add(MakePin("End", PinDirection.Output, PinType.Execution));
        n.OutputPins.Add(MakePin("Current", PinDirection.Output, PinType.Any));
        return n;
    }

    private static BuiltinFunctionNode CreateWhile()
    {
        var n = MakeFunctionNode("While");
        n.InputPins.Add(MakePin("Exec", PinDirection.Input, PinType.Execution));
        n.InputPins.Add(MakePin("Condition", PinDirection.Input, PinType.Boolean));
        n.OutputPins.Add(MakePin("Body", PinDirection.Output, PinType.Execution));
        n.OutputPins.Add(MakePin("End", PinDirection.Output, PinType.Execution));
        return n;
    }

    private static BuiltinFunctionNode CreateSwitch()
    {
        // Arm pins (label-keyed exec outputs) are added by the user after creation;
        // the initial node has Default + End.
        var n = MakeFunctionNode("Switch");
        n.InputPins.Add(MakePin("Exec", PinDirection.Input, PinType.Execution));
        n.InputPins.Add(MakePin("Selector", PinDirection.Input, PinType.Integer));
        n.OutputPins.Add(MakePin("Default", PinDirection.Output, PinType.Execution));
        n.OutputPins.Add(MakePin("End", PinDirection.Output, PinType.Execution));
        return n;
    }

    private static BuiltinFunctionNode CreateTerminator(string name)
    {
        var n = MakeFunctionNode(name);
        n.InputPins.Add(MakePin("Exec", PinDirection.Input, PinType.Execution));
        // Terminators (break/continue) have no output pins.
        return n;
    }

    // ── Helpers ──

    private static BuiltinFunctionNode MakeFunctionNode(string functionName) => new()
    {
        Id = NewId(),
        Name = functionName,
        FunctionName = functionName,
        NodeType = BlueprintNodeType.BuiltinFunction,
    };

    private static BlueprintPin MakePin(string name, PinDirection dir, PinType type)
        => new() { Id = NewId(), Name = name, Direction = dir, Type = type };

    /// <summary>Random n_XXXXXXXX id (8 uppercase hex digits) — mimics FNV-1a format.</summary>
    private static string NewId()
        => "n_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
}
