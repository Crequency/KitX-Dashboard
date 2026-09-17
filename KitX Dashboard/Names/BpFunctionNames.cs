namespace KitX.Dashboard.Names;

/// <summary>
/// Canonical BP function names for control-flow / definition nodes (D13.11).
/// Mirrors the string contract of KScript-Blueprint-Correspondence.md Appendix A;
/// the Workflow library does not expose public constants for these.
/// </summary>
internal static class BpFunctionNames
{
    internal const string Branch = "Branch";

    internal const string Each = "Each";

    internal const string While = "While";

    internal const string Switch = "Switch";

    internal const string DictNew = "DictNew";
}
