namespace KitX.Dashboard.Services;

using System;
using System.Linq;
using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// TriggerEntrySwapper — single owner of the EntryNode ↔ PluginTriggerNode swap
// (W7). The BP canvas root is an EntryNode projected from KS; when the trigger
// type is PluginEvent the editor shows a PluginTriggerNode instead. The swap is
// done IN PLACE — the node keeps the Entry's Id, coordinates and output-pin Id so
// existing connections stay valid and scope analysis sees the trigger entry.
//
// Previously this logic lived in three places: WorkflowEditorViewModelV6's
// ApplyTriggerToBlueprint / RestoreTriggerFromBlueprint and BlueprintEditorViewModelV6's
// AddPluginTriggerNode. All now delegate here.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Replaces the blueprint root EntryNode with a PluginTriggerNode (and back),
/// preserving the entry identity (Id / coordinates / output-pin Id).
/// </summary>
public static class TriggerEntrySwapper
{
    /// <summary>The blueprint's root entry node (Entry or PluginTrigger).</summary>
    public static BlueprintNode? FindEntry(Blueprint bp)
        => bp.Nodes.FirstOrDefault(n => n is EntryNode or PluginTriggerNode);

    /// <summary>
    /// Swaps the EntryNode (if any) for a <see cref="PluginTriggerNode"/> in place,
    /// preserving the entry's Id / X / Y / output-pin Id. Returns the trigger node, or
    /// null when the blueprint has no entry to replace (the caller may then add it as
    /// a new node).
    /// </summary>
    public static PluginTriggerNode? SwapToPlugin(Blueprint bp, string pluginName, string triggerName)
    {
        var entry = bp.Nodes.FirstOrDefault(n => n is EntryNode);
        if (entry is null) return null;

        var trigger = new PluginTriggerNode
        {
            Id = entry.Id,
            Name = "PluginTrigger",
            X = entry.X,
            Y = entry.Y,
            PluginName = pluginName,
            TriggerName = triggerName,
        };
        if (entry.OutputPins.Count > 0 && trigger.OutputPins.Count > 0)
            trigger.OutputPins[0].Id = entry.OutputPins[0].Id;

        var idx = bp.Nodes.IndexOf(entry);
        bp.Nodes[idx] = trigger;
        return trigger;
    }

    /// <summary>
    /// Swaps the PluginTriggerNode (if any) back to an <see cref="EntryNode"/> in place,
    /// preserving the entry's Id / X / Y / output-pin Id. Returns the removed trigger
    /// (its PluginName/TriggerName/coordinates feed the TriggerConfig), or null when the
    /// canvas root is not a trigger node.
    /// </summary>
    public static PluginTriggerNode? SwapBackToEntry(Blueprint bp)
    {
        var trigger = bp.Nodes.FirstOrDefault(n => n is PluginTriggerNode) as PluginTriggerNode;
        if (trigger is null) return null;

        var entry = new EntryNode { Id = trigger.Id, Name = "Entry", X = trigger.X, Y = trigger.Y };
        if (trigger.OutputPins.Count > 0 && entry.OutputPins.Count > 0)
            entry.OutputPins[0].Id = trigger.OutputPins[0].Id;

        var idx = bp.Nodes.IndexOf(trigger);
        bp.Nodes[idx] = entry;
        return trigger;
    }
}
