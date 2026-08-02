namespace KitX.Dashboard.Services;

using System;
using System.Collections.Generic;
using KitX.Core.Contract.Workflow;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// DefinitionValueSynchronizer — single owner of the definition-node "two value
// slots" model shared by the BP canvas and the Variable Constants panel (B7):
//   • DefaultValue — the KS-script initial value. READ-ONLY on the BP side; the
//     KS script text always keeps it (reverse projections never rewrite it).
//   • UserValue — the user's override (BP ConstValue/VarInitialValue ↔ panel
//     UserValue). Empty means "use the default".
//
// Sync directions:
//   • BP → panel (SyncBlueprintToPanel): called BEFORE Reverse / Save / Run /
//     Debug — BP-side edits must reach the panel so the overrides survive the
//     reverse projection (only the script defaults travel through the IR).
//   • Panel → BP (RestorePanelToBlueprint): called AFTER Project (KS→BP switch,
//     debug-canvas reload) — freshly rebuilt definition nodes must show the
//     user's overrides again.
//   • Panel → overrides (GetUserOverrides): collected for entries whose UserValue
//     differs from the default; injected at runtime via WorkflowOverrides.
//
// Previously this logic lived as three methods + a predicate inside
// WorkflowEditorViewModelV6 with per-node Log.Information spam; the semantics
// are unchanged, the ownership is now single.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Definition-node value semantics: BP definition nodes (const/var declarations)
/// ↔ Variable Constants panel ↔ runtime constant overrides.
/// </summary>
public static class DefinitionValueSynchronizer
{
    /// <summary>
    /// True for a definition node: the standalone const/var declaration. Usage nodes
    /// (same name, wired into data/exec edges) carry no user value and must be
    /// excluded — they would otherwise clobber the definition's override. Reads the
    /// renderer-asserted <see cref="ConstNode.IsDefinition"/>/<see cref="VariableNode.IsDefinition"/>
    /// flag (fixed at creation) instead of inferring from connectivity.
    /// </summary>
    public static bool IsDefinitionNode(BlueprintNode node)
        => node switch
        {
            ConstNode cn => cn.IsDefinition,
            VariableNode vn => vn.IsDefinition,
            _ => false,
        };

    /// <summary>
    /// BP → panel: mirrors user values from definition nodes into the panel
    /// (UserValue). Definition nodes absent from the panel are added with their
    /// default (usually empty — the user created them on the BP side).
    /// </summary>
    public static void SyncBlueprintToPanel(Blueprint bp, ICollection<VariableConstant> panel)
    {
        if (bp == null) return;
        int added = 0, updated = 0;
        foreach (var node in bp.Nodes)
        {
            if (!IsDefinitionNode(node)) continue;
            string? name = null, type = null, defaultValue = null, userValue = null;
            switch (node)
            {
                case ConstNode cn:
                    name = cn.ConstName; type = cn.ConstType;
                    defaultValue = cn.DefaultValue; userValue = cn.ConstValue;
                    break;
                case VariableNode vn when vn.VarKind == VariableKind.PubVar:
                    name = vn.VarName; type = vn.VarType;
                    defaultValue = vn.DefaultValue; userValue = vn.VarInitialValue;
                    break;
            }
            if (string.IsNullOrEmpty(name)) continue;

            var existing = FindInPanel(panel, name);
            if (existing != null)
            {
                // Always mirror the node's value — a cleared user value falls back to
                // the default (no override), otherwise the panel keeps a stale value.
                existing.UserValue = userValue ?? existing.DefaultValue;
                updated++;
            }
            else
            {
                panel.Add(new VariableConstant
                {
                    Name = name,
                    Type = type ?? "object",
                    DefaultValue = defaultValue,
                    UserValue = userValue,
                });
                added++;
            }
        }
        if (added > 0 || updated > 0)
            Log.Information("[DefValueSync] BP→panel: {Updated} updated, {Added} added", updated, added);
    }

    /// <summary>
    /// Panel → BP: mirrors the panel (UserValue, the effective initial value) onto the
    /// freshly rebuilt definition nodes, so switching KS→BP shows the user's override
    /// on the BP canvas. Written unconditionally — a cleared panel value clears the
    /// node's user value (falls back to the default).
    /// </summary>
    public static void RestorePanelToBlueprint(ICollection<VariableConstant> panel, Blueprint bp)
    {
        if (bp == null) return;
        int written = 0;
        foreach (var constant in panel)
        {
            var usr = constant.UserValue?.ToString();
            foreach (var node in bp.Nodes)
            {
                // Only the standalone definition node carries the user value; usage
                // nodes must stay untouched.
                if (!IsDefinitionNode(node)) continue;
                if (node is ConstNode cn && cn.ConstName == constant.Name)
                {
                    cn.ConstValue = usr;
                    written++;
                }
                else if (node is VariableNode vn && vn.VarName == constant.Name && vn.VarKind == VariableKind.PubVar)
                {
                    vn.VarInitialValue = usr;
                    written++;
                }
            }
        }
        if (written > 0)
            Log.Information("[DefValueSync] Panel→BP: {Written} definition nodes written", written);
    }

    /// <summary>
    /// Panel → override map: entries whose UserValue differs from the default
    /// (null when none). Injected at runtime via
    /// <c>KitX.WorkflowV6.Ir.WorkflowOverrides.ApplyConstantOverrides</c>.
    /// </summary>
    public static Dictionary<string, string?>? GetUserOverrides(ICollection<VariableConstant> panel)
    {
        if (panel.Count == 0) return null;

        var overrides = new Dictionary<string, string?>();
        foreach (var constant in panel)
        {
            var def = constant.DefaultValue?.ToString();
            var usr = constant.UserValue?.ToString();
            if (!string.Equals(def, usr, StringComparison.Ordinal))
                overrides[constant.Name] = usr;
        }
        return overrides.Count > 0 ? overrides : null;
    }

    private static VariableConstant? FindInPanel(ICollection<VariableConstant> panel, string name)
    {
        foreach (var c in panel)
            if (c.Name == name)
                return c;
        return null;
    }
}
