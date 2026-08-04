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
    /// <summary>
    /// Creates a BuiltinFunctionNode with Exec + data pins from the registry's PortSpec.
    /// DictNew is special-cased (same switch-on-name pattern as CreateControlFlowNode):
    /// it is an IR primitive definition node (a <c>var name = { ... }</c> dict literal),
    /// NOT registered in BuiltinFunctionRegistry, so no Exec pins are created and the
    /// registry lookup is skipped entirely.
    /// </summary>
    public static BuiltinFunctionNode CreateBuiltinFunctionNode(
        string functionName, BuiltinFunctionRegistry? registry)
    {
        if (functionName == "DictNew")
            return CreateDictNewDefinitionNode();

        var builtin = registry?.Get(functionName)
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

    // ── Definition / usage nodes for const & var (palette, 2026-08-03) ──

    /// <summary>
    /// Creates a const DEFINITION node (a <c>const { ... }</c> block declaration).
    /// No Exec pins — lives in the initialisation region; Reverse folds it into
    /// Workflow.Constants. Name/type/value are edited on the node (R8 declaration card).
    /// </summary>
    public static ConstNode CreateConstDefinitionNode() => new()
    {
        Id = NewId(),
        Name = "const",
        ConstName = string.Empty,
        ConstType = "int",
        IsDefinition = true,
    };

    /// <summary>
    /// Creates a var DEFINITION node (a <c>var { ... }</c> block declaration).
    /// No Exec pins; Reverse folds it into Workflow.GlobalVars.
    /// </summary>
    public static VariableNode CreateVariableDefinitionNode() => new()
    {
        Id = NewId(),
        Name = "var",
        VarName = string.Empty,
        VarType = "int",
        VarKind = VariableKind.PubVar,
        IsDefinition = true,
    };

    /// <summary>
    /// Creates a const USAGE node (a pipeline literal source). Exec in/out pins are
    /// prepended (matching BpRenderer.AddUsageNode's ordering) so the node can join the
    /// exec chain; the literal value is edited on the node (ConstValue).
    /// </summary>
    public static ConstNode CreateConstUsageNode()
    {
        var n = new ConstNode
        {
            Id = NewId(),
            Name = "literal",
            ConstName = string.Empty,
            IsDefinition = false,
        };
        n.InputPins.Insert(0, MakePin("Exec", PinDirection.Input, PinType.Execution));
        n.OutputPins.Insert(0, MakePin("Exec", PinDirection.Output, PinType.Execution));
        return n;
    }

    /// <summary>
    /// Creates a var USAGE node (a variable read/write/tap reference). Exec in/out
    /// pins prepended; the referenced VarName is picked on the node (ComboBox).
    /// </summary>
    public static VariableNode CreateVariableUsageNode()
    {
        var n = new VariableNode
        {
            Id = NewId(),
            Name = "var",
            VarName = string.Empty,
            VarKind = VariableKind.PubVar,
            IsDefinition = false,
        };
        n.InputPins.Insert(0, MakePin("Exec", PinDirection.Input, PinType.Execution));
        n.OutputPins.Insert(0, MakePin("Exec", PinDirection.Output, PinType.Execution));
        return n;
    }

    /// <summary>
    /// Creates a const USAGE node (a READ-ONLY reference to a const-block declaration).
    /// VarKind=Const and NO Value INPUT pin (matching the backend's usage shape for
    /// const references — a write into a const is structurally impossible); Value
    /// output pin carries the read. Exec in/out pins prepended so it can join the exec
    /// chain. This is a distinct palette node type from the literal ConstNode usage
    /// ("常量（字面量）").
    /// </summary>
    public static VariableNode CreateConstUsageVariableNode()
    {
        var n = new VariableNode
        {
            Id = NewId(),
            Name = "const",
            VarName = string.Empty,
            VarKind = VariableKind.Const,
            IsDefinition = false,
        };
        // Drop the descriptor-seeded Value INPUT pin (read-only reference): the
        // constructor seeds Value in + Value out; remove the input before the Exec
        // pins are prepended.
        var valueIn = n.InputPins.Find(p => p.Name == "Value" && p.Direction == PinDirection.Input);
        if (valueIn is not null) n.InputPins.Remove(valueIn);
        n.InputPins.Insert(0, MakePin("Exec", PinDirection.Input, PinType.Execution));
        n.OutputPins.Insert(0, MakePin("Exec", PinDirection.Output, PinType.Execution));
        return n;
    }

    /// <summary>
    /// Creates a DictNew DEFINITION node (a <c>var name = { ... }</c> dict literal
    /// declaration). No Exec pins — like const/var definitions it lives in the
    /// initialisation region and is NOT registered in BuiltinFunctionRegistry (IR
    /// primitive, per KScript-Blueprint-Correspondence.md). Key/value pairs are edited
    /// on the node card; each pair is a Key{i}/Value{i} input pin whose DefaultValue
    /// carries the key/value literal text. DeclKind defaults to "var"; "const" creates
    /// a const dict declaration (`const { dict d = {...} }`, legal KS — the backend
    /// renders both kinds). The initial node ships one empty pair (Key0/Value0).
    /// </summary>
    public static BuiltinFunctionNode CreateDictNewDefinitionNode(string declKind = "var")
    {
        var n = new BuiltinFunctionNode
        {
            Id = NewId(),
            Name = "DictNew",
            FunctionName = "DictNew",
            NodeType = BlueprintNodeType.BuiltinFunction,
        };
        n.Properties["DeclKind"] = declKind;
        n.Properties["DeclName"] = string.Empty;   // same default-name behaviour as CreateVariableDefinitionNode
        foreach (var (pinName, pinType) in VariadicPairHelper.DictNewSpec.EnumeratePair(0))
            n.InputPins.Add(MakePin(pinName, PinDirection.Input, pinType));
        n.OutputPins.Add(MakePin("Dict", PinDirection.Output, PinType.Dict));
        return n;
    }

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
