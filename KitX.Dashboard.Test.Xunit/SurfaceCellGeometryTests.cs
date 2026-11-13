using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// A cell's width is a share of its row, so it can be arbitrarily small; whatever is drawn inside it
/// cannot be. These tests are about that meeting point.
/// </summary>
/// <remarks>
/// <para><b>The invariant is "content must not overflow at any weight".</b> It is not "a cell must be
/// at least N pixels": the weight system exists to let a cell be narrow, so raising the floor would
/// solve the symptom by removing the feature.</para>
///
/// <para><b>The previous version of this file guarded a narrower thing.</b> Cells used to carry a row
/// of spinners and ticks whose natural width was 204px, so the template had to hide them in narrow
/// cells and the tests measured that threshold. Now a weight is set by dragging the border between
/// two cells, so a cell holds only a label and one 14px icon button — there is no fixed-width strip
/// left to hide. The first test below keeps the reason the old one existed: it asserts that nothing
/// inside a cell declares a width capable of outgrowing it, so a control added to this template later
/// fails here rather than in a screenshot.</para>
/// </remarks>
public class SurfaceCellGeometryTests
{
    /// <summary>Fixed widths declared in the cell template.</summary>
    /// <remarks>
    /// Matches every <c>Width="N"</c> rather than only one control's: the point is that a cell holds
    /// nothing big enough to overflow, whatever kind of control it is.
    /// </remarks>
    private static readonly Regex DeclaredWidth = new(
        @"\bWidth=""(?<value>\d+)""",
        RegexOptions.Compiled);

    private static readonly Regex CellTemplate = new(
        @"<DataTemplate DataType=""\{x:Type viewModels:SurfaceElementVM\}"">(?<body>[\s\S]*?)</DataTemplate>",
        RegexOptions.Compiled);

    private static readonly Regex BoundaryTemplate = new(
        @"<DataTemplate DataType=""\{x:Type viewModels:SurfaceBoundaryVM\}"">(?<body>[\s\S]*?)</DataTemplate>",
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
    public void No_child_of_a_cell_declares_a_width_that_could_outgrow_it()
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

        // The narrowest cell a row can produce is (390 - 10*8)/10 = 31px, and 16 of that is padding.
        var narrowestContent = 31 - (SurfaceElementVM.CellPadding * 2);

        var offenders = DeclaredWidth
            .Matches(template.Groups["body"].Value)
            .Select(m => int.Parse(m.Groups["value"].Value))
            .Where(width => width > narrowestContent)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"the cell template declares fixed widths of {string.Join(", ", offenders)}px, while a " +
            $"tenth-of-a-row cell offers {narrowestContent}px of content space. A weight is a share of " +
            $"its row, so a cell can be that narrow by design — nothing inside it may have a width of " +
            $"its own. Use alignment or trimming instead.");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(10)]
    public void A_border_sits_in_the_gap_between_its_two_cells(int count)
    {
        var row = Row(count);
        var cells = Enumerable.Range(0, count).Select(i => Cell(row, i)).ToList();

        for (var i = 0; i + 1 < cells.Count; i++)
        {
            var border = new SurfaceBoundaryVM(cells[i], cells[i + 1]);

            // Centred in the gap, so the grip does not lean into either neighbour.
            Assert.Equal((cells[i].Right + cells[i + 1].Left) / 2, border.Centre, 6);

            // The grab area covers the gap and only the gap: it must not overlap either cell, because
            // a press on a cell belongs to the cell.
            Assert.True(
                border.Left >= cells[i].Right - 0.001,
                $"{count} cells: the border at index {i} starts at {border.Left}, " +
                $"inside the cell ending at {cells[i].Right}");
            Assert.True(
                border.Left + border.HitWidth <= cells[i + 1].Left + 0.001,
                $"{count} cells: the border at index {i} ends at {border.Left + border.HitWidth}, " +
                $"inside the cell starting at {cells[i + 1].Left}");
        }
    }

    [Fact]
    public void The_border_template_declares_no_grip_wider_than_the_gap()
    {
        // The grip is a child of the grab area, centred in it. Wider than the gap it would paint over
        // a cell's border and read as part of that cell.
        var path = RepositoryFile(
            @"KitX Clients\KitX Dashboard\KitX Dashboard\Views\SurfaceEditorWindow.axaml");
        var template = BoundaryTemplate.Match(File.ReadAllText(path));

        Assert.True(template.Success, $"the border's DataTemplate was not found in {path}");

        var widths = DeclaredWidth
            .Matches(template.Groups["body"].Value)
            .Select(m => int.Parse(m.Groups["value"].Value))
            .ToList();

        Assert.All(
            widths,
            width => Assert.True(
                width <= SurfaceElementVM.CellGap,
                $"the border template declares a {width}px child, wider than the " +
                $"{SurfaceElementVM.CellGap}px gap it is centred in"));
    }

    [Fact]
    public void The_template_read_from_disk_is_the_one_the_running_assembly_was_built_from()
    {
        // The tests above read widths out of a file. If that file has been edited since the last
        // build, they describe source that is not what ran — they would pass against a template the
        // application does not have, and fail against one it does.
        //
        // The scope is exactly "same input, not stale": this proves the file and the assembly were
        // produced from one comparison point. It does not prove the IL matches the text. And if the
        // .axaml ever stops being a Compile item, the two mtimes lose their causal link and this
        // assertion goes quietly true forever.
        var axaml = RepositoryFile(
            @"KitX Clients\KitX Dashboard\KitX Dashboard\Views\SurfaceEditorWindow.axaml");
        var assembly = typeof(SurfaceEditorViewModel).Assembly.Location;

        var edited = File.GetLastWriteTimeUtc(axaml);
        var built = File.GetLastWriteTimeUtc(assembly);

        Assert.True(
            edited <= built,
            $"template {edited:O}, assembly {built:O} — rebuild before trusting this file");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    public void A_row_of_equal_weights_stays_inside_its_row(int count)
    {
        var row = Row(count);

        for (var i = 0; i < row.Count; i++)
        {
            var cell = Cell(row, i);

            Assert.True(cell.Left >= 0, $"cell {i} of {count} starts at {cell.Left}, left of the canvas");
            Assert.True(
                cell.Right <= SurfaceElementVM.CanvasWidth + 0.5,
                $"cell {i} of {count} ends at {cell.Right}, past the {SurfaceElementVM.CanvasWidth}px canvas");
        }
    }

    [Fact]
    public void Dragging_a_border_to_a_position_puts_the_border_there()
    {
        // The round trip is the whole feature: if dragging to x leaves the border at some other x,
        // the grip runs away from the pointer and the gesture is unusable however correct the
        // weights are.
        var editor = Editor(Row(2));
        var border = editor.Boundaries[0];

        foreach (var target in new[] { 120.0, 160.0, 200.0, 240.0 })
        {
            editor.DragBoundary(border, target);

            var landed = (editor.Elements[0].Right + editor.Elements[1].Left) / 2;

            // Within one weight step, which is the finest the format can express.
            Assert.True(
                Math.Abs(landed - target) <= SurfaceEditorViewModel.PixelsPerWeightUnit,
                $"dragged to {target}, the border landed at {landed:F1}");
        }
    }

    [Fact]
    public void Dragging_one_border_leaves_every_other_cell_where_it_was()
    {
        var row = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Weight = 3, Key = "a" },
            new SurfaceTextElement { Row = 1, Weight = 3, Key = "b" },
            new SurfaceTextElement { Row = 1, Weight = 3, Key = "c" },
        };

        var editor = Editor(row);
        var before = editor.Elements.Select(e => (e.Left, e.Width)).ToList();

        // Drag the second border (b|c), which must not move a.
        editor.DragBoundary(editor.Boundaries[1], 300);

        Assert.Equal(before[0].Item1, editor.Elements[0].Left, 3);
        Assert.Equal(before[0].Item2, editor.Elements[0].Width, 3);

        // And a cell either side of the dragged border did change, or the drag did nothing.
        Assert.NotEqual(before[1].Item2, editor.Elements[1].Width, 3);
    }

    [Fact]
    public void A_pair_of_ones_can_still_be_split_unevenly()
    {
        // The palette gives every newly placed key weight 1, so a row of two starts at 1:1 — the only
        // integer split of a total of 2. Without refining the units a deliberate drag would be a
        // no-op on exactly the rows the palette creates.
        var editor = Editor(Row(2));

        editor.DragBoundary(editor.Boundaries[0], 260);

        Assert.True(
            editor.Elements[0].Width > editor.Elements[1].Width,
            $"after dragging right, the left cell is {editor.Elements[0].Width:F1}px and the right " +
            $"is {editor.Elements[1].Width:F1}px");

        // Ratios, not absolute numbers: the file may hold 5:2 or 10:4, and both are the same card.
        var left = editor.Elements[0].Element.Weight;
        var right = editor.Elements[1].Element.Weight;
        Assert.True(left > right, $"weights are {left}:{right}, which is not the drag that happened");
        Assert.True(left >= 1 && right >= 1, $"weights are {left}:{right}; a zero part is a missing cell");
    }

    [Fact]
    public void Refining_the_units_does_not_move_the_cells_nobody_touched()
    {
        // Refining multiplies every weight on the row by the same factor, which changes no ratio. If
        // it ever multiplied only the dragged pair, the other cells would jump.
        var row = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "a" },
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "b" },
            new SurfaceTextElement { Row = 1, Weight = 1, Key = "c" },
        };

        var editor = Editor(row);
        var thirdBefore = (editor.Elements[2].Left, editor.Elements[2].Width);

        editor.DragBoundary(editor.Boundaries[0], 200);

        Assert.Equal(thirdBefore.Item1, editor.Elements[2].Left, 3);
        Assert.Equal(thirdBefore.Item2, editor.Elements[2].Width, 3);
    }

    [Fact]
    public void A_drag_never_produces_a_weight_below_one()
    {
        var editor = Editor(Row(3));

        // Far past both ends, in both directions.
        editor.DragBoundary(editor.Boundaries[0], -500);
        editor.DragBoundary(editor.Boundaries[0], 0);
        editor.DragBoundary(editor.Boundaries[1], 9999);

        Assert.All(
            editor.Elements,
            cell => Assert.True(
                cell.Element.Weight >= 1,
                $"a cell ended with weight {cell.Element.Weight}; the format forbids it and the " +
                $"renderer would read it as 1"));
    }

    /// <summary>An editor holding one document, wired to a store that keeps nothing.</summary>
    /// <remarks>
    /// The real <see cref="SurfaceEditorViewModel"/> is used rather than a stub: the drag arithmetic
    /// is exactly what these tests measure and it lives on the real type. Only the store is fake —
    /// none of these tests are about persistence, and a store that wrote files would let them fail
    /// for a reason they do not measure.
    /// </remarks>
    private static SurfaceEditorViewModel Editor(IReadOnlyList<SurfaceElement> row)
    {
        var editor = new SurfaceEditorViewModel(new NoStore(row), _ => []);
        editor.Load("test");
        return editor;
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

    /// <summary>A store that hands back one document and writes nowhere.</summary>
    private sealed class NoStore : ISurfaceStore
    {
        private readonly IReadOnlyList<SurfaceElement> _elements;

        public NoStore(IReadOnlyList<SurfaceElement> elements) => _elements = elements;

        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = _elements }, [], []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
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
