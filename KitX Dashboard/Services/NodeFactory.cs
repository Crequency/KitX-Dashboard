namespace KitX.Dashboard.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;

// ─────────────────────────────────────────────────────────────────────────────
// NodeFactory — replaces the orphaned INodeRegistry (Dashboard-Frontend-Refactor
// Handoff.md §F1.1). Non-builtin node types are created via Activator.CreateInstance
// (each Contract node subclass self-describes via GetDescriptor()); builtin function
// nodes are built from the new WorkflowIR BuiltinFunctionRegistry's PortSpec data,
// mirroring BpRenderer.BuildStatementNode's descriptor-assembly logic.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Creates BlueprintNode instances for the editor palette. Replaces INodeRegistry
/// with direct use of Contract node types + the new lib's BuiltinFunctionRegistry.
/// </summary>
public sealed class NodeFactory
{
    private static readonly Dictionary<BlueprintNodeType, Type> s_typeMap = new()
    {
        [BlueprintNodeType.Entry] = typeof(EntryNode),
        [BlueprintNodeType.PluginTrigger] = typeof(PluginTriggerNode),
        [BlueprintNodeType.Const] = typeof(ConstNode),
        [BlueprintNodeType.Call] = typeof(CallNode),
        [BlueprintNodeType.CallHelper] = typeof(CallHelperNode),
        [BlueprintNodeType.Variable] = typeof(VariableNode),
        [BlueprintNodeType.BuiltinFunction] = typeof(BuiltinFunctionNode),
    };

    private static readonly Dictionary<BlueprintNodeType, NodeDescriptor> s_descriptorCache = BuildDescriptorCache();

    private readonly BuiltinFunctionRegistry _builtinRegistry;

    public NodeFactory(BuiltinFunctionRegistry builtinRegistry)
    {
        _builtinRegistry = builtinRegistry ?? throw new ArgumentNullException(nameof(builtinRegistry));
    }

    /// <summary>Creates a new node instance of the given type with default pin configuration.</summary>
    public BlueprintNode Create(BlueprintNodeType type)
    {
        if (!s_typeMap.TryGetValue(type, out var nodeType))
            throw new ArgumentException($"Unknown node type: {type}");
        return (BlueprintNode)Activator.CreateInstance(nodeType)!;
    }

    /// <summary>Gets the cached descriptor for a non-builtin node type.</summary>
    public NodeDescriptor GetDescriptor(BlueprintNodeType type)
    {
        if (!s_descriptorCache.TryGetValue(type, out var descriptor))
            throw new ArgumentException($"No descriptor for node type: {type}");
        return descriptor;
    }

    /// <summary>All registered non-builtin node types.</summary>
    public IReadOnlySet<BlueprintNodeType> RegisteredTypes => s_typeMap.Keys.ToHashSet();

    /// <summary>
    /// Creates a BuiltinFunctionNode pre-configured from the new lib's
    /// BuiltinFunctionRegistry. Pins are built from PortSpec (the new lib's lean
    /// pin descriptor), converted to Contract PinDescriptor + BlueprintPin.
    /// </summary>
    public BlueprintNode CreateBuiltinFunctionNode(string functionName)
    {
        var def = _builtinRegistry.Get(functionName)
            ?? throw new ArgumentException($"Unknown builtin function: {functionName}");

        var node = new BuiltinFunctionNode
        {
            NodeType = BlueprintNodeType.BuiltinFunction,
            FunctionName = functionName,
            Name = functionName,  // DisplayName; the new lib's IBuiltinFunction has no DisplayName field
        };

        var inputPins = new List<PinDescriptor>();
        foreach (var p in def.InputPorts)
            inputPins.Add(new PinDescriptor(p.Name, p.Type, p.RelativeY));

        var outputPins = new List<PinDescriptor>();
        foreach (var p in def.OutputPorts)
            outputPins.Add(new PinDescriptor(p.Name, p.Type, p.RelativeY));

        var descriptor = new NodeDescriptor(
            inputPins, outputPins, functionName,
            def.InputVariadic, def.OutputVariadic);

        node.SetDescriptor(descriptor);

        foreach (var pd in descriptor.InputPins)
            node.InputPins.Add(new BlueprintPin { Name = pd.Name, Direction = PinDirection.Input, Type = pd.Type });
        foreach (var pd in descriptor.OutputPins)
            node.OutputPins.Add(new BlueprintPin { Name = pd.Name, Direction = PinDirection.Output, Type = pd.Type });

        return node;
    }

    private static Dictionary<BlueprintNodeType, NodeDescriptor> BuildDescriptorCache()
    {
        var cache = new Dictionary<BlueprintNodeType, NodeDescriptor>();
        foreach (var kvp in s_typeMap)
        {
            var instance = (BlueprintNode)Activator.CreateInstance(kvp.Value)!;
            cache[kvp.Key] = instance.GetDescriptor();
        }
        return cache;
    }
}
