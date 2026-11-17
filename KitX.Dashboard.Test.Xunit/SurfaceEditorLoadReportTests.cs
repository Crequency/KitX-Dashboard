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
/// A card that came back shorter than the file has to say so.
/// </summary>
/// <remarks>
/// <para><b>The state this guards against is a card that looks complete.</b> Reading a file drops
/// elements this build cannot draw, and the remainder is a legitimate card — so nothing crashes, no
/// exception is thrown, and the author lays out a shape that is missing a cell they wrote. A card
/// that quietly lost one looks exactly like a card that never had one, which is why the log line
/// was not enough: the log is not open while the author works.</para>
///
/// <para><b>Why the count is asserted and not just "some message".</b> The number is the claim. A
/// status line that says "something could not be loaded" every time the file has one dropped entry
/// would pass a looser test and be wrong the moment two are dropped.</para>
/// </remarks>
public class SurfaceEditorLoadReportTests
{
    /// <summary>A load that dropped an element leaves the count on the status bar.</summary>
    /// <remarks>
    /// The defect was in the load path, so this loads — it does not call a save. The two counts are
    /// asserted separately because a status line built from the wrong list would be right for the
    /// wrong reason whenever the two lists held the same number.
    /// </remarks>
    [Fact]
    public void A_load_that_dropped_elements_says_how_many_on_the_status_bar()
    {
        var editor = EditorOver(new DroppingStore(dropped: 2));

        editor.Load("load-report-test");

        Assert.Contains("2", editor.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Not saved yet", editor.StatusLine, StringComparison.Ordinal);
    }

    /// <summary>Each dropped element keeps its own sentence in the panel.</summary>
    /// <remarks>
    /// The count is the shape of the news, not its content. An author who reads "two items could not
    /// be loaded" and then looks at the panel has to find two entries there, each naming what was
    /// dropped — otherwise the number is the only thing the window ever says and there is nothing to
    /// search the file for.
    /// </remarks>
    [Fact]
    public void Every_dropped_element_appears_in_the_check_panel()
    {
        var editor = EditorOver(new DroppingStore(dropped: 2));

        editor.Load("load-report-test");

        Assert.Equal(2, editor.Diagnostics.Count);
        Assert.All(editor.Diagnostics, finding =>
            Assert.Equal(DroppingStore.DroppedCode, finding.Code));
    }

    /// <summary>A load that dropped nothing says nothing.</summary>
    /// <remarks>
    /// The other direction, because "always announce" is a plausible way to make the first two tests
    /// pass and it would put a sentence on every file that loaded cleanly.
    /// </remarks>
    [Fact]
    public void A_load_that_dropped_nothing_says_nothing()
    {
        var editor = EditorOver(new DroppingStore(dropped: 0));

        editor.Load("load-report-test");

        Assert.Empty(editor.Diagnostics);
        Assert.DoesNotContain("could not be loaded", editor.StatusLine, StringComparison.Ordinal);
    }

    /// <summary>The elements the file did have are still on the canvas.</summary>
    /// <remarks>
    /// The dropped ones are gone and the kept ones are not: this asserts the count of what is left,
    /// so a load that reported two dropped and then drew nothing would fail here rather than look
    /// like a card whose author had drawn nothing.
    /// </remarks>
    [Fact]
    public void What_the_file_did_have_is_still_on_the_canvas()
    {
        var editor = EditorOver(new DroppingStore(dropped: 2));

        editor.Load("load-report-test");

        Assert.Equal(DroppingStore.Kept, editor.Elements.Count);
    }

    private static SurfaceEditorViewModel EditorOver(ISurfaceStore store)
    {
        var editor = new SurfaceEditorViewModel(
            store, _ => [new KeyDeclaration("title", "string", KeyRole.Out)]);

        return editor;
    }

    /// <summary>A store whose load keeps one element and drops as many as it is told.</summary>
    private sealed class DroppingStore : ISurfaceStore
    {
        public const string DroppedCode = "surface.element.unsupported-type";

        public const int Kept = 3;

        private readonly int _dropped;

        public DroppingStore(int dropped) => _dropped = dropped;

        public SurfaceLoadResult Load(string toolkitId)
        {
            var kept = Enumerable
                .Range(1, Kept)
                .Select(row => (SurfaceElement)new SurfaceTextElement { Row = row, Weight = 1, Key = "title" })
                .ToList();

            var dropped = Enumerable
                .Range(1, _dropped)
                .Select(row => Diagnostic.Warning(
                    DroppedCode,
                    $"Dropped one element: no type named 'Slider' at row {row}.",
                    new DiagnosticPath(ToolkitId: toolkitId)))
                .ToList();

            return new SurfaceLoadResult(new SurfaceDocument { Elements = kept }, [], dropped);
        }

        public void Save(string toolkitId, SurfaceDocument document)
        {
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
