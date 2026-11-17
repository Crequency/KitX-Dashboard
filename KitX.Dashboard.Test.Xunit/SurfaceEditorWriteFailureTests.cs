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

    /// <summary>The bar says which file, and repeats the platform's own words.</summary>
    /// <remarks>
    /// <para><b>Both halves, because they answer different questions.</b> The path says where to go;
    /// the platform's first line says what is wrong when the author gets there. An earlier draft
    /// classified the cause into “disk full”, “no permission” and “no folder” instead — measured on
    /// this machine a read-only card throws <see cref="UnauthorizedAccessException"/> and reaches
    /// exactly one of those branches, so two thirds of that vocabulary was copy for situations nobody
    /// had observed. The platform's sentence is not a guess.</para>
    ///
    /// <para><b>The file name is the part the platform cannot supply.</b> The write composes into
    /// <c>surface.json.tmp</c> and swaps, so a failure at the swap names the temporary file — a path
    /// the author has never seen and cannot look at. That is why the store wraps the failure in
    /// <see cref="SurfaceWriteException"/>, which carries the real path.</para>
    /// </remarks>
    [Fact]
    public void The_status_bar_names_the_file_and_repeats_what_the_system_said()
    {
        var store = new FailingStore
        {
            Failure = new SurfaceWriteException(
                "write-failure-test",
                @"C:\cards\surface.json",
                new UnauthorizedAccessException("Access to the path is denied.")),
        };
        var editor = EditorOver(store);

        editor.Place(editor.DeclaredKeys[^1]);
        editor.AutoSaveIfDirty();

        Assert.Contains("surface.json", editor.StatusLine, StringComparison.Ordinal);
        Assert.Contains("denied", editor.StatusLine, StringComparison.Ordinal);

        // Not the temporary file's name: that path is real, and useless to the author.
        Assert.DoesNotContain("surface.json.tmp", editor.StatusLine, StringComparison.Ordinal);

        var refused = Assert.Single(editor.Diagnostics.Where(diagnostic =>
            diagnostic.Code == SurfaceEditorViewModel.SaveFailedCode));

        Assert.NotNull(refused.Data);
        Assert.True(refused.Data!.TryGetValue("file", out var file));
        Assert.Contains("surface.json", file, StringComparison.Ordinal);
        Assert.True(refused.Data!.TryGetValue("reason", out var reason));
        Assert.Contains("denied", reason, StringComparison.Ordinal);
    }

    /// <summary>A framework message is several lines and the bar is one.</summary>
    /// <remarks>
    /// Asserted on the helper rather than through a store, because the point is the shape of the
    /// text: a message that arrives with advice after a full stop and a line break must not put
    /// either of them on the status bar. The path's own full stop (<c>C:\Users\a.b\…</c>) must
    /// survive, which is why the split is on <c>". "</c> and not on <c>'.'</c>.
    /// </remarks>
    [Theory]
    [InlineData("Access to the path is denied.", "Access to the path is denied.")]
    [InlineData("Access is denied.\r\n   at System.IO.File.WriteAllText(...)", "Access is denied.")]
    [InlineData("disk full\nfree some space", "disk full")]
    [InlineData(@"'C:\Users\a.b\surface.json' is denied.", @"'C:\Users\a.b\surface.json' is denied.")]
    public void Only_the_first_line_of_a_message_reaches_the_bar(string message, string expected) =>
        Assert.Equal(expected, SurfaceEditorViewModel.FirstSentence(message));

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
