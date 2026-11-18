using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// A weight edit makes the last save's findings describe a document that no longer exists.
/// </summary>
/// <remarks>
/// <para><b>The defect this guards</b>: after a save that found something, dragging a border left the
/// panel showing findings about the document as it was before the drag, while the status line had
/// already moved on to "Not saved yet". The verdict was withdrawn and the evidence for it was not,
/// which is the other half of the contradiction the verdict work fixed.</para>
///
/// <para><b>Why the weight paths and not the rest.</b> Eight of the eleven places that mark the
/// canvas dirty either call <c>ForgetLastSave</c> or go through <c>Rebuild</c>. The two weight paths
/// deliberately avoid <c>Rebuild</c> — a rebuild during a drag destroys the pointer capture, which is
/// the whole reason they refresh in place — and that is exactly how they slipped past the one place
/// that sweeps.</para>
///
/// <para><b>Why not simply call <c>ForgetLastSave</c>.</b> It also recomputes the card's height and
/// clears the status line. A border drag reaches its weight change once per pointer move, so the full
/// teardown would put a height recomputation on the frame path; and it would erase the write alarm,
/// which describes the disk and which no drag can change. The narrow sweep keeps those two and drops
/// the rest, and the tests below pin both halves of that sentence.</para>
/// </remarks>
public class SurfaceEditorWeightSweepTests
{
    /// <summary>Dragging a border after a save drops the findings that spoke about the old document.</summary>
    [Fact]
    public void A_drag_after_a_save_drops_the_findings_about_the_document_it_wrote()
    {
        var store = new RecordingStore(Line(), [Problem("ghost")]);
        var editor = EditorOver(store);

        editor.Save();
        Assert.Single(editor.Diagnostics);

        Drag(editor);

        // The drag happened — without this the assertion below would pass on an editor that ignored
        // the gesture, which is indistinguishable from one that swept correctly.
        Assert.True(editor.IsDirty);
        Assert.Empty(editor.Diagnostics);
    }

    /// <summary>The same, reached through the inspector's weight setter rather than a drag.</summary>
    [Fact]
    public void Setting_a_weight_after_a_save_drops_the_findings_about_the_document_it_wrote()
    {
        var store = new RecordingStore(Line(), [Problem("ghost")]);
        var editor = EditorOver(store);

        editor.Save();
        Assert.Single(editor.Diagnostics);

        editor.SetWeightInPlace(editor.Elements[0], 7);

        Assert.True(editor.IsDirty);
        Assert.Empty(editor.Diagnostics);
    }

    /// <summary>A standing write alarm survives the drag, because a drag cannot change the disk.</summary>
    /// <remarks>
    /// This is the only justification for the narrow sweep existing at all. If someone later replaces
    /// <c>SweepStaleDiagnostics</c> with <c>ForgetLastSave</c> to remove the duplication, this is the
    /// test that goes red — and it goes red with the message an author would lose: the one saying the
    /// card cannot reach the disk.
    /// </remarks>
    [Fact]
    public void A_write_alarm_outlives_a_drag()
    {
        var store = new RecordingStore(Line(), [Problem("ghost")]) { FailSave = true };
        var editor = EditorOver(store);

        editor.SaveCommand.Execute().Subscribe();

        var alarm = Assert.Single(editor.Diagnostics);
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, alarm.Code);

        Drag(editor);

        Assert.True(editor.IsDirty);
        var survivor = Assert.Single(editor.Diagnostics);
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, survivor.Code);
    }

    /// <summary>The height finding outlives a drag, because a drag moves nothing between rows.</summary>
    /// <remarks>
    /// The other half of "narrow". A card taller than a screen is a fact about which rows exist, and
    /// a border drag changes weights on one row — it cannot make the card shorter, so the finding is
    /// still true and sweeping it would be the panel forgetting something it knows.
    /// </remarks>
    [Fact]
    public void The_height_finding_outlives_a_drag()
    {
        var store = new RecordingStore(Tall(), []);
        var editor = EditorOver(store);

        editor.Save();

        var height = Assert.Single(editor.Diagnostics);
        Assert.Equal(SurfaceEditorViewModel.TallerThanAScreenCode, height.Code);

        Drag(editor);

        Assert.True(editor.IsDirty);
        Assert.Equal(SurfaceEditorViewModel.TallerThanAScreenCode, Assert.Single(editor.Diagnostics).Code);
    }

    /// <summary>
    /// A drag does not bring a finding back, and a narrow sweep is what makes that true.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is not the same test as the one above.</b> <c>A_write_alarm_outlives_a_drag</c>
    /// proves the alarm survives. This one proves nothing <em>else</em> appears beside it -- and the
    /// difference is real rather than theoretical, because the obvious simplification of the sweep,
    /// calling <c>ForgetLastSave</c> instead, does exactly that.</para>
    ///
    /// <para>The path is the one that separates them. A failed write leaves <c>Diagnostics</c> holding
    /// the alarm alone, because <c>Save</c> clears the list and adds the store's findings before the
    /// write is attempted. From there <c>ForgetLastSave</c> keeps the alarm and then recomputes the
    /// height finding, putting it back at the front: on a card taller than a screen, a drag would
    /// resurrect a finding that the failed save had already removed. The narrow sweep only removes,
    /// so it cannot add anything back.</para>
    ///
    /// <para>That distinction used to be invisible to this file -- every test here passed under
    /// either implementation, which is why the mutation to <c>ForgetLastSave</c> went green. It was
    /// green because the write-alarm test asked only about the alarm's survival and nothing asked
    /// about the list's membership.</para>
    /// </remarks>
    [Fact]
    public void A_drag_does_not_bring_a_finding_back()
    {
        // Tall enough that a height recomputation would have something to insert: the document is
        // 896px against an 845px screen, so if the sweep recomputes, this finding reappears.
        var store = new RecordingStore(Tall(), []) { FailSave = true };
        var editor = EditorOver(store);

        editor.SaveCommand.Execute().Subscribe();

        // The failed write replaced the list with exactly the alarm, even though the card is too tall.
        var before = Assert.Single(editor.Diagnostics);
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, before.Code);

        Drag(editor);

        var after = Assert.Single(editor.Diagnostics);
        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, after.Code);
    }

    /// <summary>
    /// A drag that changes nothing leaves the findings alone.
    /// </summary>
    /// <remarks>
    /// The commit point matters: <c>DragBoundary</c> returns before touching anything when the
    /// pointer has not moved the border to a different weight. Sweeping there would make pressing a
    /// border without moving it erase the panel, which is an edit the author did not make.
    /// </remarks>
    [Fact]
    public void A_press_that_moves_no_border_leaves_the_findings_alone()
    {
        var store = new RecordingStore(Line(), [Problem("ghost")]);
        var editor = EditorOver(store);

        editor.Save();
        Assert.Single(editor.Diagnostics);

        // The border's own position: the drag asks for exactly the split the row already has.
        var boundary = Assert.Single(editor.Boundaries);
        editor.DragBoundary(boundary, boundary.Before.Right + SurfaceElementVM.CellGap / 2);

        Assert.Single(editor.Diagnostics);
    }

    private static void Drag(SurfaceEditorViewModel editor)
    {
        var boundary = Assert.Single(editor.Boundaries);

        // Far past the middle of the pair: the drag has to land on a different split, or the commit
        // point is never reached and every assertion above would be about a gesture that did nothing.
        editor.DragBoundary(boundary, boundary.Before.Right + 200);
    }

    private static SurfaceEditorViewModel EditorOver(ISurfaceStore store)
    {
        var editor = new SurfaceEditorViewModel(
            store, _ => [new KeyDeclaration("title", "string", KeyRole.Out)]);

        editor.Load("weight-sweep-test");

        return editor;
    }

    /// <summary>Two cells sharing one row, so there is a border to drag.</summary>
    private static SurfaceDocument Line() => new()
    {
        Elements =
        [
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "title" },
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "title" },
        ],
    };

    /// <summary>Enough rows to make the card taller than a screen, with one border to drag.</summary>
    /// <remarks>
    /// Fourteen slots put the card at 896px, past the 845px floor, so loading it raises the height
    /// finding. The last row holds two cells because a row of one has no border between cells -- the
    /// drag below would have nothing to grab and every assertion after it would be about a gesture
    /// that never happened. Two cells in one row still occupy one slot.
    /// </remarks>
    private static SurfaceDocument Tall() => new()
    {
        Elements =
        [
            .. Enumerable.Range(1, 13)
                .Select(row => (SurfaceElement)new SurfaceTextElement { Row = row, Weight = 1, Key = "title" }),
            new SurfaceTextElement { Row = 14, Weight = 1, Key = "title" },
            new SurfaceTextElement { Row = 14, Weight = 1, Key = "title" },
        ],
    };

    private static Diagnostic Problem(string key) => new(
        DiagnosticSeverity.Warning,
        "surface.element.unknown-key",
        $"Dropped SurfaceTextElement at row 1, weight 1: the ToolKit does not declare a key named '{key}'.",
        new DiagnosticPath(Key: key));

    /// <summary>A store that answers as told, and can be told to refuse the write.</summary>
    private sealed class RecordingStore(
        SurfaceDocument loaded,
        IReadOnlyList<Diagnostic> diagnostics) : ISurfaceStore
    {
        public bool FailSave { get; init; }

        public SurfaceLoadResult Load(string toolkitId) => new(loaded, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
            if (FailSave)
                throw new SurfaceWriteException(toolkitId, @"D:\cards\surface.json", new IOExceptionStub());
        }

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => diagnostics;

        public void Delete(string toolkitId)
        {
        }

        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }

    /// <summary>A write failure that is not an IO exception of this assembly's kind.</summary>
    private sealed class IOExceptionStub : Exception
    {
        public IOExceptionStub()
            : base("Access to the path is denied.")
        {
        }
    }
}
