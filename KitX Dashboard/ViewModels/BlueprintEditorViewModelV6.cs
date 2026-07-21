using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// BlueprintEditorViewModelV6 — empty stub for the future v6 BP editor frontend.
//
// The v5 BlueprintEditorViewModel is 2708 lines deep: it owns NodifyM node VMs,
// integrates with the SyncService (BP edits → IR diff), NodeFactory (palette),
// BpGraphLens (IR → Blueprint), debug highlight, variable-node type inference,
// scope-block rendering, etc.
//
// For v6 none of that exists yet — the KitX.WorkflowV6 library has placeholder
// BsTextLens / BpGraphLens, no structured-reduction algorithm, no concrete BP
// rendering model. This stub exists so WorkflowEditorViewModelV6 has a BlueprintVM
// property to bind in the placeholder BP canvas area. When the v6 implementation
// plan fills in BpGraphLens + the structured-reduction check (discussion notes §7),
// this stub is replaced with the real VM (likely a much leaner one than v5 thanks
// to the structured-graph constraint eliminating most edit-validation complexity).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Empty stub ViewModel for the future v6 Blueprint editor. Carries no state and no
/// commands; exists so the v6 editor window has a placeholder BlueprintVM to bind.
/// </summary>
internal partial class BlueprintEditorViewModelV6 : ObservableObject
{
    /// <summary>
    /// Banner text shown in place of a BP canvas. Replaced with a real NodifyEditor
    /// (or equivalent) when the v6 BP lens ships.
    /// </summary>
    public string PlaceholderBanner { get; } =
        "Blueprint editor for v6 grammar" + Environment.NewLine +
        "is not yet implemented." + Environment.NewLine +
        Environment.NewLine +
        "Pending: BpGraphLens (structured-graph renderer), structural-reduction" + Environment.NewLine +
        "check (§7.2), and the v6 node palette. See Package/Structured-BS-Discussion-Notes.md.";
}
