using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// A write that fails has to leave the author somewhere to go.
/// </summary>
/// <remarks>
/// <para><b>The defect is a shadow.</b> A failed write sets two things: the alarm flag, which the
/// status bar reads, and <see cref="SurfaceEditorViewModel.Status"/>, which holds the sentence
/// naming the reason. <see cref="SurfaceEditorViewModel.StatusLine"/> decides between them by asking
/// <see cref="SurfaceEditorViewModel.IsDirty"/> first — and a failed write deliberately leaves the
/// card dirty, because nothing reached the disk. So the reason is written and then covered by
/// “Not saved yet.”, which is precisely what the bar said before the failure. The window reports the
/// failure, and the report is invisible.</para>
///
/// <para><b>Why the finding in the panel is not enough.</b> It is there, and it is the fuller
/// account. But the panel is a scrolling list beside the canvas, and the bar is the line under the
/// button the author just pressed — the two are not the same act of telling. The test asserts on the
/// bar because that is the place the defect is.</para>
/// </remarks>
public class SurfaceEditorWriteFailureTests
{
    /// <summary>The status bar names the reason instead of repeating that nothing is saved.</summary>
    [Fact]
    public void A_failed_write_puts_the_reason_on_the_status_bar()
    {
        var store = new FailingStore { Failure = new IOException("the disk is full") };
        var editor = EditorOver(store);

        editor.Place(editor.DeclaredKeys[^1]);
        editor.AutoSaveIfDirty();

        Assert.True(editor.IsDirty);
        Assert.Contains("the disk is full", editor.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Not saved yet", editor.StatusLine, StringComparison.Ordinal);
    }

    /// <summary>The alarm outlives an edit, and so does the reason it carries.</summary>
    /// <remarks>
    /// The two halves fail together if the precedence is decided the other way round, so this pins
    /// the choice rather than the mechanism: an edit is not the thing that answers a failed write.
    /// </remarks>
    [Fact]
    public void An_edit_before_a_retry_does_not_cover_the_reason_again()
    {
        var store = new FailingStore { Failure = new IOException("the disk is full") };
        var editor = EditorOver(store);

        editor.Place(editor.DeclaredKeys[^1]);
        editor.AutoSaveIfDirty();

        editor.Place(editor.DeclaredKeys[0]);

        Assert.Contains("the disk is full", editor.StatusLine, StringComparison.Ordinal);
    }

    /// <summary>The same window can be retried, and a retry that lands clears the alarm.</summary>
    /// <remarks>
    /// The author's only route out of a failed write is to fix the disk and press the button again.
    /// Nothing in the window is rebuilt for that, so what this pins is that the failure left no state
    /// behind that a later success cannot clear — the flag, the sentence, and the finding all go.
    /// </remarks>
    [Fact]
    public void A_retry_in_the_same_window_succeeds_and_clears_the_alarm()
    {
        var store = new FailingStore { Failure = new IOException("the disk is full") };
        var editor = EditorOver(store);

        editor.Place(editor.DeclaredKeys[^1]);
        editor.AutoSaveIfDirty();
        Assert.Single(editor.Diagnostics);

        store.Failure = null;
        editor.Save();

        Assert.Empty(editor.Diagnostics);
        Assert.False(editor.IsDirty);
        Assert.Equal("Saved.", editor.Status);
        Assert.Equal("Saved.", editor.StatusLine);
    }

    /// <summary>The bar says what went wrong, not merely that something did.</summary>
    /// <remarks>
    /// A permission failure and a full disk are fixed by different people. The cause is asserted
    /// through the translated sentence rather than through the exception's own message, because the
    /// message is whatever the platform wrote and this window does not own it.
    /// </remarks>
    [Fact]
    public void The_status_bar_names_the_cause_in_the_readers_language()
    {
        var store = new FailingStore
        {
            Failure = new UnauthorizedAccessException("Access to the path is denied."),
        };
        var editor = EditorOver(store);

        editor.Place(editor.DeclaredKeys[^1]);
        editor.AutoSaveIfDirty();

        var refused = Assert.Single(editor.Diagnostics.Where(diagnostic =>
            diagnostic.Code == SurfaceEditorViewModel.SaveFailedCode));

        Assert.NotNull(refused.Data);
        Assert.True(refused.Data!.TryGetValue("cause", out var cause));
        Assert.Equal("not-permitted", cause);
    }

    private static SurfaceEditorViewModel EditorOver(ISurfaceStore store)
    {
        var editor = new SurfaceEditorViewModel(
            store, _ => [new KeyDeclaration("title", "string", KeyRole.Out)]);

        editor.Load("write-failure-test");

        return editor;
    }

    /// <summary>A store with one key to place, whose writes fail until it is told otherwise.</summary>
    private sealed class FailingStore : ISurfaceStore
    {
        public Exception? Failure { get; set; }

        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = [] }, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
            if (Failure is not null)
                throw Failure;
        }

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => [];

        public void Delete(string toolkitId)
        {
        }

        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
