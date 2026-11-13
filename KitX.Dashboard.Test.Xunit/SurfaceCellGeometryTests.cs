using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// A cell's width is a share of its row, so it can be arbitrarily small; the controls drawn inside it
/// have a natural width that cannot be. These tests are about that meeting point, which is where the
/// orchestrator's canvas broke: three elements on one row gave the middle cell 122px of which 106 was
/// usable, and two 72px spinners plus two ticks needed more than 160. Nothing overflowed at run time
/// in a way any assertion saw, because <see cref="SurfaceElementVM.Width"/> was correct — 122 is the
/// right width for a third of a row.
/// </summary>
/// <remarks>
/// <para><b>The invariant is "content must not overflow at any weight".</b> It is not "a cell must be
/// at least N pixels": the weight system exists to let a cell be narrow, so shrinking the floor would
/// be solving the symptom by removing the feature. The cells therefore have to be able to show less,
/// and the tests below hold both halves of that bargain — the strip is only drawn where it fits, and
/// where it is not drawn nothing inside the cell has a width of its own.</para>
///
/// <para><b>Why the template is parsed here at all.</b> The constants live in the view model and the
/// widths live in the XAML; the defect was precisely that each was consistent with itself. A test
/// that only checks <see cref="SurfaceElementVM.ShowsFullControls"/> against
/// <see cref="SurfaceElementVM.FullControlsMinimumWidth"/> would be arithmetic about two numbers in
/// one file. Reading the fixed widths out of the template is what makes the two files answer to each
/// other.</para>
/// </remarks>
public class SurfaceCellGeometryTests
{
    private static readonly Regex Spinner = new(
        @"<NumericUpDown\b[^>]*?Width=""(?<value>\d+)""",
        RegexOptions.Compiled);

    private static readonly Regex CellTemplate = new(
        @"<DataTemplate DataType=""\{x:Type viewModels:SurfaceElementVM\}"">(?<body>[\s\S]*?)</DataTemplate>",
        RegexOptions.Compiled);

    /// <summary>A row of <paramref name="count"/> equal-weight elements, all on row 1.</summary>
    private static IReadOnlyList<SurfaceElement> Row(int count, int weight = 1) =>
        [.. Enumerable.Range(0, count).Select(i => (SurfaceElement)new SurfaceTextElement
        {
            Row = 1,
            Weight = weight,
            Key = $"k{i}",
        })];

    private static SurfaceElementVM Cell(IReadOnlyList<SurfaceElement> row, int index) =>
        new(row[index], row);

    [Fact]
    public void The_widest_controls_a_cell_can_hold_are_the_ones_its_strip_needs()
    {
        var path = RepositoryFile(
            @"KitX Clients\KitX Dashboard\KitX Dashboard\Views\SurfaceEditorWindow.axaml");
        var source = File.ReadAllText(path);
        var template = CellTemplate.Match(source);

        Assert.True(
            template.Success,
            $"the cell's DataTemplate could not be found by its DataType in {path} " +
            $"({source.Length} chars read, " +
            $"{(source.Contains("viewModels:SurfaceElementVM") ? "the type name is present" : "the type name is absent")})");

        // The template contains the strip twice: once laid out inside the cell and once inside a
        // flyout. Only the first copy competes with the cell's width — the flyout is measured
        // against the window, which is the whole reason it can carry the full-size controls into a
        // 31px cell. Summing the whole template counts both and reports a width the cell never has.
        var body = template.Groups["body"].Value;
        var flyout = body.IndexOf("<Button.Flyout>", StringComparison.Ordinal);
        Assert.True(flyout > 0, "the compact route's flyout was not found in the cell template");
        var inline = body[..flyout];

        // Only the spinners compete with the cell's width. An icon's 14px is inside a button that is
        // laid out by its content, and the flyout's 200 is a MinWidth measured against the window —
        // counting either would report a width the cell never has to hold.
        var declared = Spinner.Matches(inline)
            .Select(m => int.Parse(m.Groups["value"].Value))
            .Sum();

        Assert.Equal(144, declared);

        // The flyout carries its own copy of both spinners, at the same natural width.
        Assert.Equal(2, Spinner.Matches(body[flyout..]).Count);

        // The constant has to leave room for the ticks and the margin as well, so it must be at least
        // the spinners plus enough for two icon buttons. Asserting the ordering rather than an exact
        // value keeps this a constraint rather than a copy of the constant.
        Assert.True(
            SurfaceElementVM.ControlStripNaturalWidth >= declared + 40,
            $"the strip is declared to need {SurfaceElementVM.ControlStripNaturalWidth}px while its " +
            $"two spinners alone declare {declared}px; the ticks and the margin have nowhere to go. " +
            $"Widening a spinner means widening {nameof(SurfaceElementVM.ControlStripNaturalWidth)}.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void A_row_of_equal_weights_stays_inside_its_row(int count)
    {
        var row = Row(count);

        for (var i = 0; i < row.Count; i++)
        {
            var cell = Cell(row, i);

            // No cell may start before the canvas or end past it.
            Assert.True(cell.Left >= 0, $"cell {i} of {count} starts at {cell.Left}, left of the canvas");
            Assert.True(
                cell.Left + cell.Width <= SurfaceElementVM.CanvasWidth + 0.5,
                $"cell {i} of {count} ends at {cell.Left + cell.Width}, past the {SurfaceElementVM.CanvasWidth}px canvas");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void The_inline_strip_is_only_shown_where_it_fits(int count)
    {
        var row = Row(count);

        foreach (var cell in row.Select((_, i) => Cell(row, i)))
        {
            if (!cell.ShowsFullControls)
                continue;

            // The claim the property makes, checked against the width it is computed from: when the
            // strip is shown, the space inside the cell's padding can hold it.
            Assert.True(
                cell.Width - (SurfaceElementVM.CellPadding * 2) >= SurfaceElementVM.ControlStripNaturalWidth,
                $"{count} equal weights: a {cell.Width}px cell shows the full strip, which needs " +
                $"{SurfaceElementVM.ControlStripNaturalWidth}px of content space");
        }
    }

    [Fact]
    public void The_narrowest_cell_the_editor_can_produce_takes_the_compact_route()
    {
        // Ten elements of weight 1 is the narrowest a row can get: (390 - 10*8) / 10 = 31px. This is
        // the case fleet-delta asked to be answerable and the one the old template could not render.
        var row = Row(10);
        var cell = Cell(row, 0);

        Assert.Equal(31, cell.Width, 3);
        Assert.False(
            cell.ShowsFullControls,
            $"a {cell.Width}px cell claims to fit the {SurfaceElementVM.ControlStripNaturalWidth}px strip");
    }

    [Fact]
    public void A_cell_wide_enough_for_the_strip_actually_takes_the_full_route()
    {
        // The other direction: without this, a threshold that never fires would pass the test above
        // and silently move every control into a flyout, which is a different product.
        var row = Row(1);
        var cell = Cell(row, 0);

        Assert.Equal(382, cell.Width, 3);
        Assert.True(cell.ShowsFullControls);
    }

    [Fact]
    public void A_weighted_row_midpoint_is_where_the_two_routes_divide()
    {
        // One row, two elements, weights chosen so the narrow one straddles the threshold. The point
        // is that the decision is made per cell and not per row: the wide sibling keeps its controls
        // while the narrow one does not.
        var row = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Weight = 10, Key = "wide" },
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "narrow" },
        };

        var wide = new SurfaceElementVM(row[0], row);
        var narrow = new SurfaceElementVM(row[1], row);

        Assert.True(wide.Width > narrow.Width);
        Assert.True(wide.ShowsFullControls, $"{wide.Width}px should hold the strip");
        Assert.False(narrow.ShowsFullControls, $"{narrow.Width}px should not");
    }

    /// <summary>Walks up from the test binary until it finds the solution file.</summary>
    /// <remarks>Same helper as <c>ThemeResourceKeyTests</c>; duplicated rather than shared because a
    /// shared helper is a place for one test's assumption to become another's.</remarks>
    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KitX.sln")))
                return Path.Combine(directory.FullName, relativePath);

            directory = directory.Parent;
        }

        throw new InvalidOperationException("KitX.sln was not found above " + AppContext.BaseDirectory);
    }
}
