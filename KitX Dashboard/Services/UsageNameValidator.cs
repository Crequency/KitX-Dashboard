using System;
using System.Collections.Generic;
using System.Linq;

namespace KitX.Dashboard.Services;

/// <summary>
/// Pure validation helpers for usage-node name edits (2026-08-03): a usage reference
/// must match a declared const/var/dict/Each-item name exactly (Ordinal, case-sensitive).
/// Kept as a public static helper so the rejection rule is unit-testable without the VM.
/// </summary>
public static class UsageNameValidator
{
    /// <summary>
    /// True when <paramref name="candidate"/> (trimmed, non-empty) is contained in
    /// <paramref name="declaredNames"/> under Ordinal (case-sensitive) comparison.
    /// </summary>
    public static bool IsDeclaredName(string? candidate, IEnumerable<string> declaredNames)
    {
        var trimmed = candidate?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        return declaredNames.Contains(trimmed, StringComparer.Ordinal);
    }
}
