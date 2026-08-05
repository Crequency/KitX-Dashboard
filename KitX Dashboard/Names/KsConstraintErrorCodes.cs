namespace KitX.Dashboard.Names;

/// <summary>
/// BP-side constraint error codes used by the frontend's fast pre-checks.
/// <para>
/// Mirror of <c>KitX.WorkflowV6.Lens.BpGraphLens.KsConstraintErrors</c>, which is
/// internal to the Workflow library and therefore not referenceable from the
/// Dashboard assembly. Keep in sync with the library's single source of truth
/// (Kscript-Blueprint-GrammarRule.md §4.3). D8: the frontend must never hardcode
/// these codes as string literals at call sites.
/// </para>
/// </summary>
internal static class KsConstraintErrorCodes
{
    /// <summary>E3 — Unique predecessor: each Exec input has at most one incoming edge.</summary>
    internal const string KS102 = "KS102";

    /// <summary>D2 — Single data input: each data input pin has at most one incoming edge.</summary>
    internal const string KS111 = "KS111";
}
