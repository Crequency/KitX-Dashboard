using System;
using System.Collections.Generic;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// A save verdict belongs to a document; a write alarm belongs to the disk.
/// </summary>
/// <remarks>
/// <para><b>This file guards one sentence</b>: <em>an edit retires the verdict and leaves the alarm
/// standing</em>. The defect it targets is the one that looks tidy — every edit began with
/// “forget what the last save found”, and that sweep took the failed-write alarm with it while the
/// status line kept pointing at it. The panel was empty and the window still said the card could not
/// be written.</para>
///
/// <para>The three cases are deliberately the same edit performed on three different states, because
/// the claim is not “editing clears things” — it is which things an edit is entitled to clear.</para>
/// </remarks>
public class SurfaceEditorVerdictTests
{
    /// <summary>An edit after a save that had findings leaves neither a finding nor its sentence.</summary>
    /// <remarks>
    /// Both halves are asserted together: clearing the list and leaving “Saved, with problems.” over an
    /// empty panel is the shape the author actually met, and a test that only counted findings would
    /// have called that fixed.
    /// </remarks>
    [Fact]
    public void An_edit_after_a_save_with_findings_leaves_no_verdict_behind()
    {
        var store = new AlarmStore(findings: [Problem("ghost")]);
        var editor = EditorOver(store);

        editor.Save();

        // The state the defect starts from: a finding on screen and a sentence about it above.
        Assert.Single(editor.Diagnostics);
        Assert.Equal("Saved, with problems.", editor.Status);

        editor.Place(editor.DeclaredKeys[^1]);

        Assert.Empty(editor.Diagnostics);
        Assert.Empty(editor.Status);
    }

    /// <summary>An edit after a failed write keeps the alarm, in both places it is shown.</summary>
    /// <remarks>
    /// The failure is the one piece of news the canvas cannot answer, so an edit is not allowed to
    /// retire it: the file is still unwritten and the author still has to be told. The count is asserted
    /// as exactly one so a sweep that kept the entry but duplicated it would also fail.
    /// </remarks>
    [Fact]
    public void An_edit_after_a_failed_write_keeps_the_alarm()
    {
        var store = new AlarmStore(findings: []) { SaveFailure = new IOException("the disk is full") };
        var editor = EditorOver(store);

        editor.Place(editor.DeclaredKeys[^1]);
        editor.AutoSaveIfDirty();

        var alarm = Assert.Single(editor.Diagnostics);
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, alarm.Code);
        Assert.Contains("the disk is full", editor.Status, StringComparison.Ordinal);

        editor.Place(editor.DeclaredKeys[0]);

        alarm = Assert.Single(editor.Diagnostics);
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, alarm.Code);
        Assert.Contains("the disk is full", editor.Status, StringComparison.Ordinal);
        Assert.True(editor.IsDirty);
    }

    /// <summary>A write that succeeds is what takes the alarm down — not an edit, and not time.</summary>
    /// <remarks>
    /// This pins the other end of the same rule. If the alarm were allowed to disappear on an edit, the
    /// only event that can honestly retire it would be unreachable; if it never disappeared, a transient
    /// error would leave a permanent accusation on a card that is safely on disk.
    /// </remarks>
    [Fact]
    public void A_write_that_succeeds_after_the_failure_takes_the_alarm_down()
    {
        var store = new AlarmStore(findings: []) { SaveFailure = new IOException("the disk is full") };
        var editor = EditorOver(store);

        editor.Place(editor.DeclaredKeys[^1]);
        editor.AutoSaveIfDirty();
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, Assert.Single(editor.Diagnostics).Code);

        editor.Place(editor.DeclaredKeys[0]);
        store.SaveFailure = null;
        editor.AutoSaveIfDirty();

        Assert.Empty(editor.Diagnostics);
        Assert.Empty(editor.Status);
        Assert.False(editor.IsDirty);
    }

    /// <summary>A write that reaches the disk answers an earlier alarm, however it was asked for.</summary>
    /// <remarks>
    /// The flag guarding the status line was only ever lowered by the clock and by closing. A button save
    /// that succeeded therefore left it raised, and the next edit was forbidden from emptying a status
    /// line it had already rewritten — “Saved.” standing over an empty panel, which is the defect one
    /// section up wearing a different face.
    /// </remarks>
    [Fact]
    public void A_button_save_that_succeeds_answers_an_earlier_alarm()
    {
        var store = new AlarmStore(findings: []) { SaveFailure = new IOException("the disk is full") };
        var editor = EditorOver(store);

        editor.Place(editor.DeclaredKeys[^1]);
        editor.AutoSaveIfDirty();
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, Assert.Single(editor.Diagnostics).Code);

        store.SaveFailure = null;
        editor.Save();

        Assert.Empty(editor.Diagnostics);
        Assert.Equal("Saved.", editor.Status);

        // The assertion the defect violates: the alarm is gone, so an edit must now leave no sentence
        // behind, exactly as it does when no alarm was ever raised.
        editor.Place(editor.DeclaredKeys[0]);

        Assert.Empty(editor.Diagnostics);
        Assert.Empty(editor.Status);
    }

    /// <summary>The button reports a failed write instead of throwing it at the window.</summary>
    /// <remarks>
    /// <para>This is asserted on <see cref="SurfaceEditorViewModel.SaveCommand"/> and not on
    /// <see cref="SurfaceEditorViewModel.Save"/>, because the gap was never in the method: the method
    /// threw, and the command had nothing listening, so the framework's default handler wrote one log
    /// line and the window stayed silent. A test that called Save() and caught the exception would
    /// pass against the broken build.</para>
    ///
    /// <para>The subscription stands in for every place the framework would have handled it. Nothing
    /// arriving there is the claim: the failure was reported in the window, not dropped on the floor.</para>
    /// </remarks>
    [Fact]
    public void A_button_save_that_fails_says_so_instead_of_escaping()
    {
        var store = new AlarmStore(findings: []) { SaveFailure = new IOException("the disk is full") };
        var editor = EditorOver(store);
        editor.Place(editor.DeclaredKeys[^1]);

        Exception? escaped = null;
        using var subscription = editor.SaveCommand.ThrownExceptions.Subscribe(ex => escaped = ex);

        editor.SaveCommand.Execute().Subscribe();

        Assert.Null(escaped);
        Assert.Contains("the disk is full", editor.Status, StringComparison.Ordinal);
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, Assert.Single(editor.Diagnostics).Code);
        Assert.True(editor.IsDirty);
    }

    private static SurfaceEditorViewModel EditorOver(ISurfaceStore store)
    {
        var editor = new SurfaceEditorViewModel(
            store, _ => [new KeyDeclaration("title", "string", KeyRole.Out)]);

        editor.Load("verdict-test");

        return editor;
    }

    private static Diagnostic Problem(string key) => new(
        DiagnosticSeverity.Warning,
        "surface.element.unknown-key",
        $"Dropped SurfaceTextElement at row 1, weight 1: the ToolKit does not declare a key named '{key}'.",
        new DiagnosticPath(Key: key));

    /// <summary>A store with one key to place, answers with findings, and can be told to fail.</summary>
    private sealed class AlarmStore(IReadOnlyList<Diagnostic> findings) : ISurfaceStore
    {
        public Exception? SaveFailure { get; set; }

        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = [] }, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
            if (SaveFailure is not null)
                throw SaveFailure;
        }

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => findings;

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
