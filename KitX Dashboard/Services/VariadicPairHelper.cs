using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;

namespace KitX.Dashboard.Services;

// ─────────────────────────────────────────────────────────────────────────────
// VariadicPairHelper — pure index arithmetic for PAIRED variadic pin groups (T8).
//
// DictNew's Key{i}/Value{i} inputs grow one full pair at a time. The group index
// computation lives here, free of UI/ViewModel dependencies, so the unit test
// project (KitX.Dashboard.Test.Xunit) can assert the pairing arithmetic directly.
//
// The hardcoded DictNew spec also lives here: DictNew is an IR primitive (NOT in
// BuiltinFunctionRegistry, see NodeFactoryV6), so TryExpandVariadicSide falls back
// to this spec when the registry lookup misses.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Pure helper for paired variadic pin groups (e.g. DictNew's Key(String)/Value(Any)
/// pairs growing as Key0/Value0, Key1/Value1, ...).
/// </summary>
public static class VariadicPairHelper
{
    /// <summary>
    /// The hardcoded paired variadic spec for DictNew: prefixes ["Key","Value"] at
    /// types [String,Any], index starting at 0. DictNew is an IR primitive definition
    /// node and is deliberately absent from BuiltinFunctionRegistry, so consumers
    /// fall back to this spec instead of a registry lookup.
    /// </summary>
    public static VariadicPinSpec DictNewSpec => new("", 0, PinType.Any)
    {
        PinNamePrefixes = ["Key", "Value"],
        PinTypes = [PinType.String, PinType.Any],
    };

    /// <summary>
    /// Computes the next group index for a paired variadic group: the largest numeric
    /// suffix already present in <paramref name="existingPinNames"/> under any of
    /// <paramref name="prefixes"/>, plus one — clamped to be at least
    /// <paramref name="startIndex"/>. Example: with prefixes ["Key","Value"] and
    /// existing "Key0"/"Value0"/"Key1"/"Value1", the next index is 2.
    /// </summary>
    public static int ComputeNextIndex(IReadOnlyList<string> prefixes, int startIndex,
        IEnumerable<string> existingPinNames)
    {
        var max = startIndex - 1;
        foreach (var name in existingPinNames)
        {
            foreach (var prefix in prefixes)
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var suffix = name.Substring(prefix.Length);
                if (suffix.Length == 0 || !suffix.All(char.IsAsciiDigit)) continue;
                if (int.TryParse(suffix, out var idx) && idx > max) max = idx;
            }
        }
        return max + 1;
    }

    /// <summary>
    /// Yields the (pinName, pinType) for every prefix of the paired spec at the given
    /// group index — e.g. ("Key1", String), ("Value1", Any). Callers use it to build
    /// or append a full pair of pins (Contract + VM double-write).
    /// </summary>
    public static IEnumerable<(string Name, PinType Type)> EnumeratePair(this VariadicPinSpec spec, int index)
    {
        for (var i = 0; i < spec.PinNamePrefixes!.Length; i++)
            yield return (spec.PinNamePrefixes[i] + index, spec.PinTypes![i]);
    }
}
