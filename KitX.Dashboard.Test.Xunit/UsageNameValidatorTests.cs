using System;
using System.Collections.Generic;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.Services;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Unit tests for UsageNameValidator (2026-08-03): the pure name-rejection rule behind
/// usage-node name edits — a usage reference must match a declared const/var/dict/Each
/// item name exactly (Ordinal case-sensitivity).
/// </summary>
public class UsageNameValidatorTests
{
    private static readonly IReadOnlyList<string> Declared = ["counter", "guessNum", "dictName"];

    [Fact]
    [Trait("Category", "Unit")]
    public void IsDeclaredName_ExactMatch_ReturnsTrue()
    {
        Assert.True(UsageNameValidator.IsDeclaredName("counter", Declared));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsDeclaredName_UnknownName_ReturnsFalse()
    {
        Assert.False(UsageNameValidator.IsDeclaredName("notDeclared", Declared));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsDeclaredName_CaseSensitive_ReturnsFalseOnCaseMismatch()
    {
        // Ordinal comparison — "Counter" must NOT match "counter".
        Assert.False(UsageNameValidator.IsDeclaredName("Counter", Declared));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsDeclaredName_SurroundingWhitespace_IsTrimmedBeforeMatch()
    {
        Assert.True(UsageNameValidator.IsDeclaredName("  counter  ", Declared));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsDeclaredName_NullOrBlank_ReturnsFalse()
    {
        Assert.False(UsageNameValidator.IsDeclaredName(null, Declared));
        Assert.False(UsageNameValidator.IsDeclaredName("", Declared));
        Assert.False(UsageNameValidator.IsDeclaredName("   ", Declared));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsDeclaredName_DictNewDeclName_MatchesLikeAnyOtherDeclaration()
    {
        // DictNew DeclNames join the same DefinitionNames pool (KS130) — a usage node
        // may reference a dict, so its DeclName must be accepted by the validator.
        Assert.True(UsageNameValidator.IsDeclaredName("dictName", Declared));
    }
}
