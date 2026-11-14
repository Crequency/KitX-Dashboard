using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The editor's memory of what is on disk: the dirty mark, the thirty-second write-behind, and what
/// closing the window does with a card that has not been written.
/// </summary>
/// <remarks>
/// <para><b>What this file guards.</b> Before it, the card reached the disk only when the author pressed
/// "Save and check", and closing the window threw away everything since that press without a word. S10
/// splits the single save into four moments — editing, the clock, the button, the close — and each one
/// is a different promise about what happens to the file.</para>
///
/// <para><b>Why the mark exists at all.</b> Two of those moments are decisions ("is there anything to
/// write?"), and a decision made by comparing documents is a decision that goes wrong the first time a
/// comparison is by reference. So the tests hold the flag to its two-way contract: every edit raises it,
/// only a write clears it, and a write that threw clears nothing.</para>
///
/// <para><b>The clock is tested through the method it calls, not by waiting thirty seconds.</b> The
/// window owns the timer (it is a property of the screen being open) and this file checks that the
/// window started it; what the tick does is <see cref="SurfaceEditorViewModel.AutoSaveIfDirty"/>.</para>
/// </remarks>
public class SurfaceEditorSaveClockTests
{
    [Fact]
    public void A_freshly_loaded_card_is_not_dirty()
    {
        var editor = Editor();

        Assert.False(editor.IsDirty);
    }

    /// <summary>Every edit is an edit, whichever property it changes.</summary>
    /// <remarks>
    /// A theory rather than one fact per kind, because the failure this guards against is an edit site
    /// that forgot to mark: the list has to be complete, and a new kind of edit belongs in it. Each
    /// case starts from the same two-element card, so the only difference between the cases is what was
    /// changed.
    /// </remarks>
    [Theory]
    [InlineData("field")]
    [InlineData("weight")]
    [InlineData("drag")]
    [InlineData("row")]
    [InlineData("place")]
    [InlineData("add-kind")]
    [InlineData("remove")]
    [InlineData("clear")]
    public void Every_edit_marks_the_card_dirty(string edit)
    {
        var editor = Editor();
        var first = editor.Elements[0];

        switch (edit)
        {
            case "field":
                editor.Select(first);
                Field(editor, "format").Value = "date";
                break;

            case "weight":
                editor.SetWeightInPlace(first, 3);
                break;

            case "drag":
                // Leftwards, past the first cell's midpoint: dragging the last cell to where it
                // already is would be a no-op, and a no-op is deliberately not an edit.
                editor.BeginReorder(editor.Elements[1]);
                editor.ReorderTo(20, RowCentre(1));
                editor.CommitReorder();
                break;

            case "row":
                editor.MoveToRow(first, 2);
                break;

            case "place":
                editor.Place(editor.DeclaredKeys[^1]);
                break;

            case "add-kind":
                editor.AddKind("Button");
                break;

            case "remove":
                editor.Remove(first);
                break;

            case "clear":
                editor.Clear();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(edit), edit, "no such edit in this test");
        }

        Assert.True(editor.IsDirty, $"{edit} did not mark the card dirty");
    }

    /// <summary>A refusal is not an edit.</summary>
    /// <remarks>
    /// The weight field refuses a value below one and says so; if the mark were set before the refusal
    /// was decided, the clock would spend the next thirty seconds writing a card that did not change.
    /// </remarks>
    [Fact]
    public void A_refused_edit_does_not_mark_the_card_dirty()
    {
        var editor = Editor();

        editor.SetWeightInPlace(editor.Elements[0], 0);

        Assert.False(editor.IsDirty);
        Assert.NotNull(editor.ValidationMessage);
    }

    [Fact]
    public void A_successful_save_clears_the_mark()
    {
        var store = new RecordingStore(SampleDocument(), findings: []);
        var editor = Editor(store);

        editor.SetWeightInPlace(editor.Elements[0], 2);
        Assert.True(editor.IsDirty);

        editor.Save();

        Assert.False(editor.IsDirty);
        Assert.Equal(1, store.Saves);
    }

    /// <summary>A save that threw leaves the canvas as the only copy of the work.</summary>
    /// <remarks>
    /// The flag is cleared after the write, not before it, and this is the assertion that notices the
    /// difference: a card marked clean while nothing reached the disk is a card the author will close
    /// without being asked.
    /// </remarks>
    [Fact]
    public void A_save_that_throws_leaves_the_card_dirty()
    {
        var store = new RecordingStore(SampleDocument(), findings: []);
        var editor = Editor(store);

        editor.SetWeightInPlace(editor.Elements[0], 2);
        store.SaveFailure = new IOException("the disk is full");

        Assert.Throws<IOException>(() => editor.Save());

        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void Auto_save_writes_only_when_the_card_has_changed()
    {
        var store = new RecordingStore(SampleDocument(), findings: []);
        var editor = Editor(store);

        editor.AutoSaveIfDirty();
        Assert.Equal(0, store.Saves);

        editor.SetWeightInPlace(editor.Elements[0], 2);
        editor.AutoSaveIfDirty();
        Assert.Equal(1, store.Saves);
        Assert.False(editor.IsDirty);

        // Nothing changed since the write, so the next tick has nothing to do.
        editor.AutoSaveIfDirty();
        Assert.Equal(1, store.Saves);
    }

    /// <summary>The clock writes; it does not ask whether the card renders.</summary>
    /// <remarks>
    /// S10's reason is the panel, and this is the mechanical half of it: a validator called every thirty
    /// seconds would replace the findings the author is reading with a fresh verdict, or pop a box over
    /// the canvas. The count is asserted, not the absence of a message, because the message would only
    /// show that this particular build forgot to draw it.
    /// </remarks>
    [Fact]
    public void Auto_save_does_not_run_the_validator()
    {
        var store = new RecordingStore(SampleDocument(), findings: []);
        var editor = Editor(store);

        editor.SetWeightInPlace(editor.Elements[0], 2);
        editor.AutoSaveIfDirty();

        Assert.Equal(0, store.DiagnoseCalls);
        Assert.Empty(editor.Diagnostics);
    }

    /// <summary>A card that cannot reach the disk says so, and stays dirty.</summary>
    /// <remarks>
    /// This is the one automatic action allowed to speak. The failure goes to the status line, which the
    /// author sees now, and to the check panel, which is the only list still holding it a minute later —
    /// and the mark stays raised, so the next tick tries again instead of pretending the work is safe.
    /// </remarks>
    [Fact]
    public void A_failed_auto_save_is_visible_and_keeps_the_card_dirty()
    {
        var store = new RecordingStore(SampleDocument(), findings: [])
        {
            SaveFailure = new IOException("the disk is full"),
        };

        var editor = Editor(store);

        editor.SetWeightInPlace(editor.Elements[0], 2);
        editor.AutoSaveIfDirty();

        Assert.True(editor.IsDirty);
        Assert.Contains("the disk is full", editor.Status);
        Assert.Single(editor.Diagnostics);
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, editor.Diagnostics[0].Code);

        // The next tick succeeds: the alarm comes down, so a transient disk error does not leave a
        // permanent accusation on a card that is now safely written.
        store.SaveFailure = null;
        editor.AutoSaveIfDirty();

        Assert.False(editor.IsDirty);
        Assert.Empty(editor.Diagnostics);
        Assert.Empty(editor.Status);
    }

    [Fact]
    public void Closing_a_clean_card_needs_no_confirmation()
    {
        var store = new RecordingStore(SampleDocument(), findings: []);
        var editor = Editor(store);

        var verdict = editor.PrepareToClose();

        Assert.Equal(SurfaceEditorViewModel.SurfaceCloseVerdict.Close, verdict);
        Assert.Equal(0, store.Saves);
    }

    /// <summary>The write comes before the question, and a card with findings is still written.</summary>
    /// <remarks>
    /// The order is the user's rule from 2026-11-13 turned into a sequence: a warning must never cost the
    /// file. If the validator ran first and the write were conditional on it, one bad cell would throw
    /// away a whole afternoon of laying out.
    /// </remarks>
    [Fact]
    public void Closing_writes_before_it_asks_the_validator()
    {
        var store = new RecordingStore(SampleDocument(), findings: [Findings()]);
        var editor = Editor(store);

        editor.SetWeightInPlace(editor.Elements[0], 2);

        var verdict = editor.PrepareToClose();

        Assert.Equal(["save", "diagnose"], store.Order);
        Assert.Equal(SurfaceEditorViewModel.SurfaceCloseVerdict.ConfirmFindings, verdict);
        Assert.Single(editor.Diagnostics);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Closing_with_findings_on_an_untouched_card_asks_too()
    {
        var store = new RecordingStore(SampleDocument(), findings: [Findings()]);
        var editor = Editor(store);

        var verdict = editor.PrepareToClose();

        Assert.Equal(SurfaceEditorViewModel.SurfaceCloseVerdict.ConfirmFindings, verdict);
        Assert.Equal(["diagnose"], store.Order);
    }

    [Fact]
    public void A_write_that_fails_on_close_keeps_the_window_open()
    {
        var store = new RecordingStore(SampleDocument(), findings: [])
        {
            SaveFailure = new IOException("the disk is full"),
        };

        var editor = Editor(store);
        editor.SetWeightInPlace(editor.Elements[0], 2);

        var verdict = editor.PrepareToClose();

        Assert.Equal(SurfaceEditorViewModel.SurfaceCloseVerdict.WriteFailed, verdict);
        Assert.True(editor.IsDirty);
        Assert.Contains("the disk is full", editor.Status);

        // Nothing was asked of the validator: there is no document on disk for it to describe.
        Assert.Equal(0, store.DiagnoseCalls);
    }

    [Fact]
    public void The_subtitle_shows_the_name_the_toolkit_gave_itself()
    {
        var editor = Editor(nameOf: _ => "AI Assistant");

        Assert.Equal("AI Assistant", editor.DisplayName);
        Assert.Equal("toolkit-1", editor.ToolkitId);
    }

    [Fact]
    public void The_subtitle_falls_back_to_the_id()
    {
        var editor = Editor(nameOf: _ => null);

        Assert.Equal("toolkit-1", editor.DisplayName);
    }

    [Fact]
    public void The_subtitle_does_not_show_the_same_value_twice()
    {
        var editor = Editor(nameOf: _ => "toolkit-1");

        Assert.Equal("toolkit-1", editor.DisplayName);
    }

    [AvaloniaFact]
    public void The_window_shows_the_toolkit_name_not_its_id()
    {
        var editor = Editor(nameOf: _ => "AI Assistant");

        var window = new SurfaceEditorWindow(editor, "toolkit-1");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var subtitle = window.FindControl<TextBlock>("SurfaceSubtitle");

        Assert.NotNull(subtitle);
        Assert.Equal("AI Assistant", subtitle!.Text);
    }

    /// <summary>
    /// The canvas is a card on the page, not a rectangle the colour of the page.
    /// </summary>
    /// <remarks>
    /// <para>The old pair of brushes was the window's own layer fill and a 1px secondary stroke, so the
    /// sheet had no edge at all and the author could not tell where the card ended.</para>
    ///
    /// <para><b>Measured as identity, not as taste.</b> The background has to be a different brush from
    /// the layer fill it sat invisibly on, the stroke has to exist, and the shadow has to be drawn — three
    /// facts that a future "clean up the brushes" change can be checked against, and none of which is a
    /// colour anybody has to agree with.</para>
    /// </remarks>
    [AvaloniaFact]
    public void The_canvas_is_drawn_as_a_card_on_the_page()
    {
        var editor = Editor();
        var window = new SurfaceEditorWindow(editor, "toolkit-1");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var canvas = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("canvas"));

        var fill = Assert.IsAssignableFrom<ISolidColorBrush>(canvas.Background);

        Assert.True(fill.Color.A > 0, "the card's fill is transparent");
        Assert.True(canvas.BoxShadow.Count > 0, "the card casts no shadow");

        // A stroke too faint to see is not a boundary. The pair that made the sheet invisible drew a 6%
        // black hairline, which sits inside the noise of the page around it; anything under a quarter
        // opaque is treated as absent so a later "tidy the brushes" change fails here, not in a review.
        var stroke = Assert.IsAssignableFrom<ISolidColorBrush>(canvas.BorderBrush);
        Assert.True(stroke.Color.A >= 0x40, $"the card's edge is {stroke.Color.A:x2} alpha — too faint to see");

        // The brush that made the sheet invisible: same object, same pixels.
        Assert.NotSame(window.FindResource("LayerFillColorDefaultBrush"), canvas.Background);
    }

    [AvaloniaFact]
    public void The_window_starts_the_write_behind_clock()
    {
        var editor = Editor();
        var window = new SurfaceEditorWindow(editor, "toolkit-1");

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.AutoSaveClock);
        Assert.Equal(TimeSpan.FromSeconds(30), window.AutoSaveClock!.Interval);
        Assert.True(window.AutoSaveClock.IsEnabled);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(window.AutoSaveClock);
    }

    [AvaloniaFact]
    public void The_leave_dialog_offers_leaving_and_going_back()
    {
        var dialog = new SurfaceLeaveDialog("surface.element.unknown-key: no such key");
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        var leave = dialog.FindControl<Button>("SurfaceLeaveLeave");
        var fix = dialog.FindControl<Button>("SurfaceLeaveFix");

        Assert.NotNull(leave);
        Assert.NotNull(fix);
        Assert.Equal("Leave anyway", leave!.Content);
        Assert.Equal("Go back and fix", fix!.Content);

        dialog.Close();
    }

    private static double RowCentre(int row) =>
        SurfaceElementVM.RowOrigin + ((row - 1) * SurfaceElementVM.RowHeight) + (SurfaceElementVM.RowHeight / 2);

    private static SurfaceFieldVM Field(SurfaceEditorViewModel editor, string label) =>
        editor.Fields.Single(field => field.Label == label);

    private static IReadOnlyList<SurfaceElement> SampleDocument() =>
    [
        new SurfaceTextElement { Row = 1, Weight = 1, Key = "k0" },
        new SurfaceTextElement { Row = 1, Weight = 1, Key = "k1" },
    ];

    private static Diagnostic Findings() => new(
        DiagnosticSeverity.Warning,
        "surface.element.unknown-key",
        "the ToolKit does not declare a key named 'k0'.",
        new DiagnosticPath(Key: "k0"));

    private static SurfaceEditorViewModel Editor(
        RecordingStore? store = null,
        Func<string, string?>? nameOf = null)
    {
        var editor = new SurfaceEditorViewModel(
            store ?? new RecordingStore(SampleDocument(), findings: []),
            _ =>
            [
                new KeyDeclaration("k0", "string", KeyRole.Out),
                new KeyDeclaration("k1", "string", KeyRole.Out),
            ],
            nameOf);

        editor.Load("toolkit-1");
        return editor;
    }

    /// <summary>A store that remembers what was asked of it, in order, and can be told to fail.</summary>
    private sealed class RecordingStore(IReadOnlyList<SurfaceElement> elements, Diagnostic[] findings) : ISurfaceStore
    {
        public int Saves { get; private set; }

        public int DiagnoseCalls { get; private set; }

        public List<string> Order { get; } = [];

        public Exception? SaveFailure { get; set; }

        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = [.. elements] }, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
            Order.Add("save");

            if (SaveFailure is not null)
                throw SaveFailure;

            Saves++;
        }

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys)
        {
            Order.Add("diagnose");
            DiagnoseCalls++;
            return findings;
        }

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
