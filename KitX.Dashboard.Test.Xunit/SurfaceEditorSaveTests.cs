using System;
using System.Collections.Generic;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Saving writes the canvas even when the card has problems, and then says what they are.
/// </summary>
/// <remarks>
/// <para><b>This file guards one sentence</b>: <em>saving must never be blocked by a warning,
/// because a blocked save loses work</em>. Every assertion here targets the version of
/// <see cref="SurfaceEditorViewModel.Save"/> that looks careful — "the card is wrong, do not
/// overwrite the file" — and is in fact the version that throws away a layout the author can no
/// longer recover.</para>
///
/// <para><b>Order is part of the contract, not an implementation detail.</b> The write has to
/// happen before the diagnosis is even asked for, so that no validator can take the file with it —
/// not a slow one, not a throwing one, not one wired to the wrong key table. That is why the fake
/// store records the sequence rather than only the outcome: a save that writes correctly and
/// diagnoses first would satisfy every other assertion here and still be the wrong shape.</para>
///
/// <para><b>A fake store, and honestly so.</b> <c>SurfaceValidator</c> is internal to
/// <c>KitX.ToolKit</c> and this assembly cannot see it, so the diagnostics are supplied rather than
/// produced. What the editor owes is a contract with <see cref="ISurfaceStore"/>: ask, then report.
/// Whether the real store wires that question to the validator is a test for the toolkit's own
/// assembly — this file would keep passing if that wiring were deleted, and it should be read with
/// that limit in mind.</para>
/// </remarks>
public class SurfaceEditorSaveTests
{
    [Fact]
    public void A_card_with_a_problem_is_written_anyway_and_the_problem_is_named()
    {
        var store = new RecordingStore(Documented(), [Problem("ghost")]);
        var editor = EditorOver(store);

        editor.Save();

        // The write happened. This is the assertion the defect violates: an early return on a
        // non-empty diagnosis leaves Saved null and takes the author's canvas with it.
        Assert.NotNull(store.Saved);
    }

    [Fact]
    public void What_is_written_is_the_canvas_not_the_document_validation_would_keep()
    {
        var store = new RecordingStore(Documented(), [Problem("ghost")]);
        var editor = EditorOver(store);

        editor.Save();

        // Both cells, including the one the diagnostic is about. Pruning before writing would make
        // the message describe cells that are already gone from the file.
        Assert.NotNull(store.Saved);
        Assert.Equal(2, store.Saved!.Elements.Count);
        Assert.Equal("title", store.Saved.Elements[0].Key);
        Assert.Equal("ghost", store.Saved.Elements[1].Key);
    }

    [Fact]
    public void The_problem_is_reported_in_the_editors_own_words()
    {
        var store = new RecordingStore(Documented(), [Problem("ghost")]);
        var editor = EditorOver(store);

        editor.Save();

        Assert.True(editor.HasValidationMessage);
        Assert.Contains("ghost", editor.ValidationMessage, StringComparison.Ordinal);

        // "with problems", not "did not save": the status is the one sentence an author reads
        // before closing the window.
        Assert.Equal("Saved, with problems.", editor.Status);
    }

    [Fact]
    public void The_write_comes_before_the_question()
    {
        var store = new RecordingStore(Documented(), [Problem("ghost")]);
        var editor = EditorOver(store);

        editor.Save();

        Assert.Equal(["save", "diagnose"], store.Order);

        // And the question was asked with the editor's declared keys, not with a list it invented.
        Assert.NotNull(store.DiagnosedAgainst);
        Assert.Equal("title", Assert.Single(store.DiagnosedAgainst!).Name);
    }

    [Fact]
    public void A_card_with_nothing_wrong_says_nothing()
    {
        var store = new RecordingStore(Documented(), []);
        var editor = EditorOver(store);

        editor.Save();

        Assert.NotNull(store.Saved);
        Assert.False(editor.HasValidationMessage);
        Assert.Equal("Saved.", editor.Status);
    }

    private static SurfaceEditorViewModel EditorOver(ISurfaceStore store)
    {
        var editor = new SurfaceEditorViewModel(
            store, _ => [new KeyDeclaration("title", "string", KeyRole.Out)]);

        // Loaded rather than assembled by hand: it is the only path that sets the ToolKit id, and
        // Save() refuses a card that belongs to no ToolKit — which is a different guard from this one.
        editor.Load("save-test");

        return editor;
    }

    private static SurfaceDocument Documented() => new()
    {
        Elements =
        [
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "title" },
            new SurfaceTextElement { Row = 2, Weight = 1, Key = "ghost" },
        ],
    };

    private static Diagnostic Problem(string key) => new(
        DiagnosticSeverity.Warning,
        "surface.element.unknown-key",
        $"Dropped SurfaceTextElement at row 2, weight 1: the ToolKit does not declare a key named '{key}'.",
        new DiagnosticPath(Key: key));

    /// <summary>A store that answers with the diagnostics it was given and remembers what it was asked.</summary>
    /// <remarks>
    /// The sequence is recorded as words rather than as a callback flag, because the claim under test
    /// is about order and a boolean cannot express "diagnose happened, but after the write".
    /// </remarks>
    private sealed class RecordingStore(
        SurfaceDocument loaded,
        IReadOnlyList<Diagnostic> diagnostics) : ISurfaceStore
    {
        public SurfaceDocument? Saved { get; private set; }

        public List<string> Order { get; } = [];

        public IReadOnlyList<KeyDeclaration>? DiagnosedAgainst { get; private set; }

        public SurfaceLoadResult Load(string toolkitId) => new(loaded, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
            Order.Add("save");
            Saved = document;
        }

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys)
        {
            Order.Add("diagnose");
            DiagnosedAgainst = [.. keys];

            return diagnostics;
        }

        public void Delete(string toolkitId)
        {
        }

        // Explicitly empty: the editor must not need a change notification to save, and a field
        // event nobody raises would be a compiler warning pretending the hook were used.
        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
