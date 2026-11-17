using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
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

    /// <summary>The panel names the row, not only the number of things missing.</summary>
    /// <remarks>
    /// <para><b>Why a count is not enough.</b> “Two items could not be loaded” tells the author to go
    /// looking. It does not tell them where to look, and a card of a dozen rows gives a dozen
    /// places to check against the file by eye. The row is the thing they typed and the thing they
    /// can go and edit, so the sentence has to carry it.</para>
    ///
    /// <para><b>Read through the same path the panel reads.</b> The assertion formats the shipped
    /// <c>zh-cn</c> sentence with the values the producer recorded — not against a sentence written
    /// into this test. A test that spells out the sentence here would agree with the dictionary by
    /// construction and would keep passing if the producer stopped recording <c>row</c>; this one
    /// fails on either.</para>
    /// </remarks>
    [AvaloniaFact]
    public void The_panel_names_the_row_a_dropped_element_came_from()
    {
        var editor = EditorOver(new DroppingStore(dropped: 2));
        editor.Load("load-report-test");

        var dictionary = Assert.IsType<ResourceDictionary>(AvaloniaRuntimeXamlLoader.Load(
            File.ReadAllText(RepositoryFile(
                Path.Combine(@"KitX Clients\KitX Dashboard\KitX Dashboard", "Languages", "zh-cn.axaml")))));

        Assert.True(dictionary.TryGetResource("Text_Bench_Diag_element_unsupported_type", null,
            out var found));
        var sentence = Assert.IsType<string>(found);

        // The two entries say row 1 and row 2; the numbers are distinct on purpose, so a panel that
        // printed the same one twice cannot pass.
        foreach (var (diagnostic, row) in editor.Diagnostics.Zip(["1", "2"]))
        {
            var line = SurfaceDiagnosticText.Fill(sentence, diagnostic.Data);

            Assert.Contains($"第 {row} 行", line, StringComparison.Ordinal);
            Assert.Contains("Slider", line, StringComparison.Ordinal);
            Assert.DoesNotContain("{", line, StringComparison.Ordinal);
        }
    }

    private static SurfaceEditorViewModel EditorOver(ISurfaceStore store)
    {
        var editor = new SurfaceEditorViewModel(
            store, _ => [new KeyDeclaration("title", "string", KeyRole.Out)]);

        return editor;
    }

    private static string RepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KitX.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relativePath);
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
                    $"Dropped element at row {row}: no type named 'Slider'.",
                    new DiagnosticPath(ToolkitId: toolkitId),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["row"] = row.ToString(CultureInfo.InvariantCulture),
                        ["type"] = "Slider",
                        ["reason"] = "no type named 'Slider'",
                    }))
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
