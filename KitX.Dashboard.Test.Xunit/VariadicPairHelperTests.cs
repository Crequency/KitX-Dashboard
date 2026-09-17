using System.Collections.Generic;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.Services;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Unit tests for VariadicPairHelper (T8): the pure index arithmetic behind DictNew's
/// Key{i}/Value{i} paired variadic pin expansion.
/// </summary>
public class VariadicPairHelperTests
{
    private static readonly IReadOnlyList<string> KeyValuePrefixes = ["Key", "Value"];

    [Fact]
    [Trait("Category", "Unit")]
    public void ComputeNextIndex_EmptyGroup_ReturnsStartIndex()
    {
        var index = VariadicPairHelper.ComputeNextIndex(KeyValuePrefixes, 0, []);
        Assert.Equal(0, index);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ComputeNextIndex_AfterFirstPair_ReturnsNextIndex()
    {
        var index = VariadicPairHelper.ComputeNextIndex(KeyValuePrefixes, 0, ["Key0", "Value0"]);
        Assert.Equal(1, index);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ComputeNextIndex_AfterMultiplePairs_ReturnsNextIndex()
    {
        var index = VariadicPairHelper.ComputeNextIndex(
            KeyValuePrefixes, 0, ["Key0", "Value0", "Key1", "Value1"]);
        Assert.Equal(2, index);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ComputeNextIndex_IgnoresUnrelatedNamesAndNonNumericSuffixes()
    {
        var index = VariadicPairHelper.ComputeNextIndex(
            KeyValuePrefixes, 0, ["Key0", "Value0", "KeyWord", "Exec", "Value2"]);
        Assert.Equal(3, index);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DictNewSpec_EnumeratesOneFullPair()
    {
        var pair = new List<(string Name, PinType Type)>(
            VariadicPairHelper.DictNewSpec.EnumeratePair(1));
        Assert.Equal(2, pair.Count);
        Assert.Equal("Key1", pair[0].Name);
        Assert.Equal(PinType.String, pair[0].Type);
        Assert.Equal("Value1", pair[1].Name);
        Assert.Equal(PinType.Any, pair[1].Type);
    }
}
