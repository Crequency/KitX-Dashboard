using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Lays out one ToolKit's card on a fixed 390 × 845 grid.
/// </summary>
/// <remarks>
/// <para><b>The canvas shows no runtime value, and that is the point.</b> An author who can see the
/// current value of the key behind an element will start laying out around today's data — a long
/// book title, a two-hundred-line list — and the layout stops describing the card and starts
/// describing one afternoon of it. The size being fixed makes the same argument a second time: you
/// are laying out for a phone you cannot see the contents of.</para>
///
/// <para><b>It reads declarations, not values.</b> Every element the palette offers is a key the
/// ToolKit declares and nothing has placed yet — the palette is "what is left", not "what kinds of
/// control exist". That ordering is deliberate: starting from control types produces a card whose
/// contents are whatever happened to be convenient to render.</para>
///
/// <para><b>Saving goes through <see cref="ISurfaceStore"/>.</b> This class must never learn the
/// layout file's name: that file has exactly one writer, and it is the store (ADR-0016 D3). The
/// absence is checkable — a grep for the file name anywhere under the Dashboard project must come
/// back empty, comments included, so that the criterion never needs a human to decide.</para>
///
/// <para><b>A weight is edited by dragging a border, not by typing a number.</b> That is S4 in
/// <c>Package/ToolKit-设计-03-Surface.md</c>: "drag inside a row to reorder, and to change
/// <c>weight</c>". The earlier build of this window offered spinners with a tick to apply them, plus
/// a sentence explaining that the canvas would not move until the tick was pressed. That sentence
/// existed because the two-step model existed, and the two-step model existed because the value was
/// being typed instead of placed. A drag has no "entered but not applied" state: it ends where the
/// pointer is released, and that end is the committed value — so the draft properties, the apply
/// commands and the sentence are all gone rather than shortened.</para>
/// </remarks>
public sealed partial class SurfaceEditorViewModel : ViewModelBase
{
    /// <summary>The canvas width in logical pixels. Fixed, not derived from the window.</summary>
    public const double CanvasWidth = 390;

    /// <summary>The canvas height in logical pixels.</summary>
    public const double CanvasHeight = 845;

    private readonly ISurfaceStore _store;
    private readonly Func<string, IReadOnlyList<KeyDeclaration>> _keysOf;
    private readonly Func<string, string?>? _nameOf;

    private string _toolkitId = string.Empty;
    private string _status = string.Empty;
    private string? _validationMessage;
    private string _displayName = string.Empty;
    private bool _isDirty;

    /// <summary>Creates the editor.</summary>
    /// <param name="store">Where layouts are read and written.</param>
    /// <param name="keysOf">Supplies a ToolKit's declared keys, by id.</param>
    /// <param name="nameOf">
    /// Supplies a ToolKit's display name, by id, or null when there is none. Optional because the id
    /// is a complete answer and every test already had one; the display name only makes the subtitle
    /// readable.
    /// </param>
    public SurfaceEditorViewModel(
        ISurfaceStore store,
        Func<string, IReadOnlyList<KeyDeclaration>> keysOf,
        Func<string, string?>? nameOf = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(keysOf);

        _store = store;
        _keysOf = keysOf;
        _nameOf = nameOf;

        InitCommands();
    }

    /// <summary>Every key the ToolKit declares.</summary>
    public ObservableCollection<KeyDeclaration> DeclaredKeys { get; } = [];

    /// <summary>The declared keys that nothing has placed on the canvas yet.</summary>
    public ObservableCollection<KeyDeclaration> UnplacedKeys { get; } = [];

    /// <summary>The cells on the canvas, in declaration order.</summary>
    public ObservableCollection<SurfaceElementVM> Elements { get; } = [];

    /// <summary>The draggable borders between neighbouring cells.</summary>
    /// <remarks>
    /// A separate collection rather than a property on each cell, because a border belongs to a
    /// <em>pair</em>: putting it on the left cell would make every cell ask about its neighbour, and
    /// the last cell on a row would have to know that it has no border.
    /// </remarks>
    public ObservableCollection<SurfaceBoundaryVM> Boundaries { get; } = [];

    /// <summary>Where the cell being dragged would land, or hidden when nothing is being dragged.</summary>
    public SurfaceDropIndicatorVM DropIndicator { get; } = new();

    /// <summary>The interactive kinds the palette can place.</summary>
    /// <remarks>
    /// <para><b>A second section, not a replacement for the key list.</b> The key list answers "what
    /// is declared and not yet shown"; this one answers "the card needs something to press". They are
    /// different questions and the order between them is the point: the keys come first, so a card is
    /// built out of what the ToolKit actually publishes rather than out of the controls that happened
    /// to be listed in a palette. What the previous build lacked was not an argument against control
    /// kinds — it was any way at all to place one, which made every card read-only.</para>
    ///
    /// <para><b>Only the five that are meaningfully placed by hand.</b> <c>Text</c> and <c>List</c>
    /// come from the key list, because they need a key and a key is what that list has. The other five
    /// are the ones a person adds on purpose: a button to press, an input to type into, and the three
    /// values that ride along with whatever that button sends.</para>
    /// </remarks>
    public IReadOnlyList<string> PaletteKinds { get; } = ["Button", "Input", "Number", "Select", "Switch"];

    private SurfaceElementVM? _selected;

    /// <summary>The cell currently held by the pointer, or null when no drag is in progress.</summary>
    private SurfaceElementVM? _reordering;

    /// <summary>Where the last pointer move said the dragged cell would land: row, and place in it.</summary>
    private int _reorderRow;
    private int _reorderIndex;

    /// <summary>Whether the pointer has ever moved during this drag.</summary>
    /// <remarks>
    /// Kept apart from <see cref="_reordering"/> because they answer different questions: a press
    /// sets the cell, and only a move names a landing point. Without the second flag, releasing a
    /// cell that was pressed and never moved would commit whatever the initial row and index happened
    /// to be — a reorder nobody asked for, from a gesture that was a click.
    /// </remarks>
    private bool _hasDropTarget;

    /// <summary>The cell the inspector describes, or null when nothing is selected.</summary>
    public SurfaceElementVM? Selected
    {
        get => _selected;
        private set
        {
            if (ReferenceEquals(_selected, value))
                return;

            if (_selected is not null)
                _selected.IsSelected = false;

            _selected = value;

            if (_selected is not null)
                _selected.IsSelected = true;

            this.RaisePropertyChanged(nameof(Selected));
            this.RaisePropertyChanged(nameof(HasSelection));
        }
    }

    /// <summary>Whether a cell is selected, and therefore whether there is anything to inspect.</summary>
    public bool HasSelection => _selected is not null;

    /// <summary>The selected element's properties, one field each.</summary>
    /// <remarks>
    /// Rebuilt on every selection change and never patched in place: the set of fields <em>is</em> a
    /// function of the element's kind, so keeping stale fields around would leave a <c>label</c> row
    /// under a <c>Text</c> element that has no label.
    /// </remarks>
    public ObservableCollection<SurfaceFieldVM> Fields { get; } = [];

    /// <summary>Selects a cell, or clears the inspector when given null.</summary>
    /// <param name="element">The cell to inspect, or null.</param>
    public void Select(SurfaceElementVM? element)
    {
        if (ReferenceEquals(_selected, element))
            return;

        Selected = element;

        BuildFields();
    }

    /// <summary>The ToolKit being edited.</summary>
    public string ToolkitId
    {
        get => _toolkitId;
        private set => this.RaiseAndSetIfChanged(ref _toolkitId, value);
    }

    /// <summary>The ToolKit's display name, or its id when it has none.</summary>
    /// <remarks>
    /// <para><b>The id is a GUID, and a subtitle is read by a person.</b> <see cref="ToolkitId"/> stays
    /// the id because it is what the store is keyed by; this is the same identity said the way the
    /// ToolKit names itself (<see cref="ToolkitMeta.Name"/>), which is what the author recognises on
    /// the card.</para>
    ///
    /// <para><b>One string, not "name (id)".</b> A ToolKit whose name and id are the same value — the
    /// common case for anything made before ids were assigned — would otherwise print it twice. Falling
    /// back to the id when there is no name keeps the subtitle from being blank, and printing it only
    /// once keeps a hand-made ToolKit from looking like two different ones.</para>
    /// </remarks>
    public string DisplayName
    {
        get => _displayName;
        private set => this.RaiseAndSetIfChanged(ref _displayName, value);
    }

    /// <summary>
    /// Whether the card has changes that are not on disk.
    /// </summary>
    /// <remarks>
    /// <para><b>The mark is what makes the other decisions cheap.</b> The clock writes only when this
    /// is true, closing only asks the validator when there is something to write, and a save clears
    /// it. Without it every one of those would have to compare documents, and comparing is exactly
    /// the work that goes wrong when one field is compared by reference.</para>
    ///
    /// <para><b>Set from the edits, cleared from the write.</b> It is not derived from
    /// <see cref="Diagnostics"/> or from <see cref="Status"/>: a card with no findings is not
    /// necessarily saved, and a card with findings is not necessarily unsaved — those are two
    /// different facts, and S10 needs this one.</para>
    /// </remarks>
    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    /// <summary>What the last save reported.</summary>
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>The editor's own refusal, or null when it has nothing to refuse.</summary>
    /// <remarks>
    /// <para><b>Not where a save's findings go.</b> This holds what the editor itself would not do —
    /// a card over the element limit, a row below one — and those are single, immediate, and about
    /// one gesture. What a save finds is a list, and it lives in <see cref="Diagnostics"/>.</para>
    ///
    /// <para>The two used to share this property, joined with spaces into one paragraph. Three
    /// problems then arrived as one run-on sentence with no way to tell where one ended: the
    /// structured half of a <see cref="Diagnostic"/> — its code — never reached the screen at
    /// all.</para>
    /// </remarks>
    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _validationMessage, value);
            this.RaisePropertyChanged(nameof(HasValidationMessage));
        }
    }

    /// <summary>Whether there is anything to show in the check panel.</summary>
    public bool HasValidationMessage => !string.IsNullOrEmpty(_validationMessage);

    /// <summary>What the last save found, one entry per problem.</summary>
    /// <remarks>
    /// <para><b>A collection, because a save can find several and they are separate claims.</b> Each
    /// entry keeps its <see cref="Diagnostic.Code"/>: the code is the part a reader can act on
    /// twice, and the part a future fix can attach an action to.</para>
    ///
    /// <para>Replaced on every save rather than appended to — the panel describes the file as it is
    /// now, not a history of what was wrong with it.</para>
    /// </remarks>
    public ObservableCollection<Diagnostic> Diagnostics { get; } = [];

    /// <summary>Loads a ToolKit's current layout.</summary>
    /// <param name="toolkitId">The ToolKit to edit.</param>
    public void Load(string toolkitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitId);

        ToolkitId = toolkitId;
        DisplayName = _nameOf?.Invoke(toolkitId) is { Length: > 0 } name ? name : toolkitId;
        Status = string.Empty;

        // What was on disk a moment ago is, by definition, what is on disk now.
        IsDirty = false;

        ForgetLastSave();

        // Nothing of the previous card survives a load, including the cell a drag might be holding:
        // the refused-file branch below empties the canvas without going through Rebuild.
        CancelReorder();

        DeclaredKeys.Clear();
        foreach (var key in _keysOf(toolkitId))
            DeclaredKeys.Add(key);

        var loaded = _store.Load(toolkitId);

        // A refused file is reported, not silently replaced: overwriting it from here would destroy
        // whatever the author had before this build could read it.
        if (loaded.Errors.Count > 0)
        {
            ValidationMessage = string.Join(" ", loaded.Errors.Select(e => e.Message));
            Elements.Clear();
            Boundaries.Clear();
        }
        else
        {
            Rebuild(loaded.Document.Elements);
        }

        if (loaded.Warnings.Count > 0)
            Log.Warning("[SurfaceEditor] {Count} element(s) were dropped loading '{Toolkit}'",
                loaded.Warnings.Count, toolkitId);

        RefreshUnplaced();
    }

    /// <summary>The elements currently on the canvas, in declaration order.</summary>
    private List<SurfaceElement> CurrentElements() => [.. Elements.Select(e => e.Element)];

    /// <summary>Replaces the whole canvas from a new document.</summary>
    /// <remarks>
    /// Used by the operations that add, remove or re-row an element, all of which change which
    /// cells are on screen. A weight change does <em>not</em> go through here — see
    /// <see cref="SetWeightInPlace"/> for why a drag may not rebuild the collection.
    /// </remarks>
    private void Rebuild(IReadOnlyList<SurfaceElement> elements)
    {
        // A rebuild replaces every cell, so a drag in flight is holding an object that is no longer on
        // the canvas — and the drop line would be pointing at a document that no longer exists. This
        // is the one place that knows every path which replaces the cells.
        CancelReorder();

        // The panel described the document as it was before this call, so it goes first: a saved
        // warning about a cell the author has just deleted is the editor arguing with the canvas.
        ForgetLastSave();

        // And so does the inspector: every cell is about to be replaced, so a field still pointing at
        // one of the old objects would be editing a cell that is no longer on the canvas. Callers
        // that put something new on the card select it again after this returns.
        Select(null);

        var document = elements as IReadOnlyList<SurfaceElement> ?? [.. elements];

        Elements.Clear();
        foreach (var element in document)
            Elements.Add(new SurfaceElementVM(element, document));

        RebuildBoundaries();
    }

    /// <summary>Drops what the last save found, because the document it described has changed.</summary>
    private void ForgetLastSave()
    {
        Diagnostics.Clear();
        ValidationMessage = null;
    }

    /// <summary>Records that the canvas no longer matches what is on disk.</summary>
    /// <remarks>
    /// One place, because every edit has to say it and no edit may say it differently: the flag is
    /// what the clock and the close path branch on, so an edit that forgot it would be an edit the
    /// editor silently discards on exit.
    /// </remarks>
    private void MarkDirty() => IsDirty = true;

    /// <summary>Recomputes the border between every pair of neighbours on a row.</summary>
    private void RebuildBoundaries()
    {
        Boundaries.Clear();

        foreach (var row in Elements.GroupBy(e => e.Element.Row))
        {
            var cells = row.ToList();
            for (var i = 0; i + 1 < cells.Count; i++)
                Boundaries.Add(new SurfaceBoundaryVM(cells[i], cells[i + 1]));
        }
    }

    /// <summary>Changes one element's share of its row, redrawing the cells in place.</summary>
    /// <param name="element">The cell to resize.</param>
    /// <param name="weight">How many parts of the row it should take. One or more.</param>
    /// <remarks>
    /// <para><b>In place, because a drag has a duration.</b> An earlier version replaced the
    /// declaration and rebuilt the collection on every call. That was fine when a weight arrived
    /// once, from a button; it is fatal during a drag, because the control holding the pointer
    /// capture would be destroyed on the first pixel of movement. So the cells are refreshed instead
    /// of replaced — same objects, new values.</para>
    ///
    /// <para><b>Refused here, tolerated there.</b> A weight below one is not accepted, while
    /// <see cref="SurfaceValidator"/> repairs it to one and reports a warning. The asymmetry is
    /// deliberate: the editor must not let an author write a value that will be reported back at
    /// them, and the loading path must accept whatever somebody else already wrote. Do not "unify"
    /// these two checks — they answer different questions.</para>
    /// </remarks>
    public void SetWeightInPlace(SurfaceElementVM element, int weight)
    {
        if (element is null)
            return;

        if (weight < 1)
        {
            // Refused, and said out loud: silence here would be indistinguishable from a drag that
            // simply did nothing.
            ValidationMessage = TranslateText("Bench_Surface_WeightOutOfRange")
                ?? "Weight must be at least 1.";
            return;
        }

        var index = Elements.IndexOf(element);
        if (index < 0)
            return;

        var changed = CurrentElements();
        changed[index] = changed[index] with { Weight = weight };

        // Every cell on the row is refreshed, not only the one whose number changed: a cell's Left
        // is the sum of its earlier siblings' widths, so one weight change moves all of them.
        var affectedRow = changed[index].Row;
        for (var i = 0; i < Elements.Count; i++)
        {
            if (changed[i].Row == affectedRow)
                Elements[i].Refresh(changed[i], changed);
        }

        // Refreshed, not rebuilt: during a drag this border is holding the pointer capture, and a
        // replaced control has lost it. That is also why the collection only changes shape when the
        // set of rows does — see RebuildBoundaries.
        foreach (var boundary in Boundaries)
            boundary.Refresh();

        ValidationMessage = null;
        MarkDirty();
    }

    /// <summary>How many canvas pixels one weight unit should be worth, during a drag.</summary>
    /// <remarks>
    /// A row of two cells at weight 1 and 1 has no room to move: the pair's total is 2, so the only
    /// integer split is 1/1. Dragging it would do nothing, and "nothing happened" is the worst
    /// possible answer to a deliberate gesture. So a drag may refine the row's units — see
    /// <see cref="DragBoundary"/> — and this is how fine a unit should be: about sixteen pixels,
    /// which is a fifth of the narrowest cell and a comfortable target for a mouse.
    /// </remarks>
    public const double PixelsPerWeightUnit = 16;

    /// <summary>Drags the border between two cells to a canvas position.</summary>
    /// <param name="boundary">The border being dragged.</param>
    /// <param name="canvasX">Where the pointer is, in canvas pixels.</param>
    /// <remarks>
    /// <para><b>Only the two adjacent weights move.</b> The pair keeps its combined share of the
    /// row, and every other cell keeps its own — so dragging one border never disturbs a cell the
    /// pointer is not on. Redistributing the whole row proportionally would look identical at the
    /// start of the drag and would quietly change cells the author did not touch.</para>
    ///
    /// <para><b>Why the row's units get scaled.</b> A weight is an integer, so a pair summing to 2
    /// can only be split 1/1 however far the pointer travels. That is not a rare case: it is what
    /// the palette creates, because a newly placed key gets weight 1. To let the drag actually
    /// express 5:2, the pair's total is raised to a multiple of itself that gives roughly one unit
    /// per <see cref="PixelsPerWeightUnit"/> pixels, <em>and every other weight on the row is
    /// multiplied by the same factor</em>. Multiplying by a common factor changes no ratio, so the
    /// other cells neither move nor change size; only the numbers in the file grow, and they are
    /// ratios, so nothing downstream can tell.</para>
    /// </remarks>
    public void DragBoundary(SurfaceBoundaryVM boundary, double canvasX)
    {
        if (boundary is null)
            return;

        var left = boundary.Before;
        var right = boundary.After;

        var index = Elements.IndexOf(left);
        if (index < 0 || Elements.IndexOf(right) != index + 1)
            return;

        var row = Elements.Where(e => e.Element.Row == left.Element.Row).ToList();
        if (row.Count < 2)
            return;

        var shareable = SurfaceElementVM.ShareableWidth(row.Count);
        if (shareable <= 0)
            return;

        var totalWeight = row.Sum(e => Math.Max(1, e.Element.Weight));
        var leftWeight = Math.Max(1, left.Element.Weight);
        var rightWeight = Math.Max(1, right.Element.Weight);
        var pairWeight = leftWeight + rightWeight;

        // Weight of everything before the left cell; it does not move during this drag.
        var weightBefore = row.Take(index).Sum(e => Math.Max(1, e.Element.Weight));

        var pixelsThroughLeft = canvasX - (SurfaceElementVM.CellGap * (index + 1));
        var wantedD = (totalWeight * pixelsThroughLeft / shareable) - weightBefore;

        // How much of the pair the left cell should take, as a fraction. Taken from the pointer
        // before any rounding, so the finer unit below divides the right interval.
        var fraction = Math.Clamp(wantedD / pairWeight, 0, 1);

        var pairPixels = left.Width + SurfaceElementVM.CellGap + right.Width;
        var wantedUnits = (int)Math.Round(pairPixels / PixelsPerWeightUnit);
        var factor = Math.Max(1, (int)Math.Ceiling(wantedUnits / (double)pairWeight));

        // Guard against a pathological row growing without bound: a factor is only worth applying
        // when the resulting numbers stay in a range a person could still read in the file.
        if ((long)totalWeight * factor > 100_000)
            factor = 1;

        var pairTotal = pairWeight * factor;
        var newLeft = (int)Math.Round(fraction * pairTotal);
        newLeft = Math.Clamp(newLeft, 1, pairTotal - 1);
        var newRight = pairTotal - newLeft;

        if (newLeft == leftWeight && newRight == rightWeight)
            return;

        // Applied as one edit rather than two, so a single drag step produces one consistent
        // document: scaling the others between the two SetWeightInPlace calls would briefly double
        // the scale factor.
        var changed = CurrentElements();
        for (var i = 0; i < changed.Count; i++)
        {
            if (changed[i].Row != left.Element.Row)
                continue;

            var weight = Math.Max(1, changed[i].Weight);

            if (ReferenceEquals(Elements[i], left))
                changed[i] = changed[i] with { Weight = newLeft };
            else if (ReferenceEquals(Elements[i], right))
                changed[i] = changed[i] with { Weight = newRight };
            else if (factor > 1)
                changed[i] = changed[i] with { Weight = weight * factor };
        }

        var affectedRow = left.Element.Row;
        for (var i = 0; i < Elements.Count; i++)
        {
            if (changed[i].Row == affectedRow)
                Elements[i].Refresh(changed[i], changed);
        }

        // Refreshed, not rebuilt: during a drag this border is holding the pointer capture, and a
        // replaced control has lost it. That is also why the collection only changes shape when the
        // set of rows does — see RebuildBoundaries.
        foreach (var border in Boundaries)
            border.Refresh();

        ValidationMessage = null;
        MarkDirty();
    }

    /// <summary>Whether a cell is being dragged to another place on the card.</summary>
    public bool IsReordering => _reordering is not null;

    /// <summary>Starts dragging one cell to another place on the card.</summary>
    /// <param name="cell">The cell whose body was pressed.</param>
    /// <remarks>
    /// <para><b>Nothing is written here.</b> The cell stays where it is until the pointer is released.
    /// That is the same rule the border drag follows — a drag has no committed value until it ends —
    /// and it is also what keeps the gesture readable: moving the cell on every pointer move would
    /// change the very positions the next frame is measured against, so it would chase the pointer
    /// instead of following it.</para>
    ///
    /// <para><b>The cell is selected here, not left to the window.</b> The inspector describes this
    /// cell for the whole gesture, and the press that began the drag is the only evidence there is
    /// about which cell that is.</para>
    /// </remarks>
    public void BeginReorder(SurfaceElementVM cell)
    {
        if (cell is null || Elements.IndexOf(cell) < 0)
            return;

        _reordering = cell;
        _hasDropTarget = false;
        DropIndicator.Hide();

        Select(cell);
    }

    /// <summary>Points the drop line at where the pointer is, without moving anything.</summary>
    /// <param name="canvasX">Pointer x, in canvas pixels.</param>
    /// <param name="canvasY">Pointer y, in canvas pixels.</param>
    /// <remarks>
    /// Called on every pointer move, so it must stay a pure statement about the pointer: the document
    /// is read, never rewritten. That is what lets the cell that is holding the pointer capture
    /// survive the whole drag — see <see cref="CommitReorder"/> for the write that happens at the end.
    /// </remarks>
    public void ReorderTo(double canvasX, double canvasY)
    {
        if (_reordering is null)
            return;

        _reorderRow = TargetRow(canvasY);
        _reorderIndex = InsertionIndex(_reorderRow, canvasX);
        _hasDropTarget = true;

        DropIndicator.Show(
            LineLeft(_reorderRow, _reorderIndex),
            RowTop(_reorderRow),
            SurfaceElementVM.RowHeight - SurfaceElementVM.CellGap);
    }

    /// <summary>Drops the dragged cell where the pointer left it.</summary>
    /// <remarks>
    /// <para><b>A drop that changes nothing writes nothing.</b> Releasing a cell over its own place is
    /// how a press that never became a drag ends, and it must leave the document, the check panel and
    /// the selection exactly as they were: the card is byte-for-byte the one already on disk, so there
    /// is nothing to save, nothing to report and nothing to re-describe.</para>
    ///
    /// <para><b>Rows are not renumbered.</b> The row a cell leaves keeps its number even when it is
    /// now empty. §10.2.1.2 fixed what a row number means — an order, not a distance — and an empty
    /// row has height zero, so compacting the numbers would change nothing on screen while rewriting
    /// every row below the hole.</para>
    ///
    /// <para><b>Committed in place, never rebuilt.</b> The collection is reordered under the same
    /// objects: the dragged cell is the one the inspector is describing, and a rebuild would replace
    /// it — and its selection — with new objects for the same declarations. The last frame of a drag
    /// has to land on the object the first frame picked up; that is also why the border drag
    /// refreshes instead of rebuilding, taken one step further.</para>
    /// </remarks>
    public void CommitReorder()
    {
        var cell = _reordering;
        var hasTarget = _hasDropTarget;
        var row = _reorderRow;
        var index = _reorderIndex;

        _reordering = null;
        _hasDropTarget = false;
        DropIndicator.Hide();

        // A press with no move: the gesture was a selection, and there is no landing point in it.
        if (cell is null || !hasTarget)
            return;

        var from = Elements.IndexOf(cell);

        // The cell can be gone: a click that removed it mid-drag arrives between the two events.
        if (from < 0)
            return;

        // The order the collection will have once the cell is taken out. Built before anything is
        // moved, so a drop that changes nothing can be recognised before the first write.
        var remaining = new List<SurfaceElementVM>(Elements.Count - 1);
        for (var i = 0; i < Elements.Count; i++)
        {
            if (i != from)
                remaining.Add(Elements[i]);
        }

        var onTargetRow = remaining.Where(e => e.Element.Row == row).ToList();

        int insertAt;
        if (onTargetRow.Count == 0)
        {
            // An empty row (or the new row below the last one): it goes where the row would be in the
            // declaration order, which keeps the file grouped by row rather than by edit history.
            insertAt = remaining.FindIndex(e => e.Element.Row > row);
            if (insertAt < 0)
                insertAt = remaining.Count;
        }
        else if (index < onTargetRow.Count)
        {
            insertAt = remaining.IndexOf(onTargetRow[index]);
        }
        else
        {
            insertAt = remaining.IndexOf(onTargetRow[^1]) + 1;
        }

        var before = CurrentElements();

        var after = new List<SurfaceElement>(remaining.Count + 1);
        foreach (var element in remaining)
            after.Add(element.Element);

        after.Insert(insertAt, cell.Element with { Row = row });

        // Value equality, because that is what "nothing changed" means for this file: two cells with
        // identical declarations swapping places produce the same JSON, and that drag is a no-op too.
        if (after.SequenceEqual(before))
            return;

        Elements.Move(from, insertAt);

        var document = CurrentElements();
        document[insertAt] = document[insertAt] with { Row = row };

        // Every cell is refreshed, not only the two rows involved: the rows the cell left and joined
        // both redistribute their widths, and the cell itself changed its top edge.
        for (var i = 0; i < Elements.Count; i++)
            Elements[i].Refresh(document[i], document);

        RebuildBoundaries();
        ForgetLastSave();
        ValidationMessage = null;
        MarkDirty();

        // The inspector's "row" field is a value copied out of the declaration, so a drag that landed
        // on another row would leave the field saying the number the cell no longer has.
        BuildFields();
    }

    /// <summary>Abandons the drag: the cell stays where it is and the line goes away.</summary>
    /// <remarks>
    /// Reached when the pointer capture is taken rather than released — another window taking the
    /// pointer, the cell being deleted by a click that was not part of the drag, a rebuild. Without it
    /// the drop line would stay on the canvas pointing at nothing.
    /// </remarks>
    public void CancelReorder()
    {
        _reordering = null;
        _hasDropTarget = false;
        DropIndicator.Hide();
    }

    /// <summary>The row the pointer's vertical position is over, or the new row below the last one.</summary>
    /// <remarks>
    /// Bands are counted from the first row's top edge, because a row's pixels are a function of its
    /// number and nothing else (§10.2.1.2: row numbers order, they do not measure distance). A pointer
    /// above the top edge belongs to row one; one below the lowest row in use belongs to a new row
    /// underneath it — which is the only way to append a row by dragging.
    /// </remarks>
    private int TargetRow(double canvasY)
    {
        var band = (int)Math.Floor((canvasY - SurfaceElementVM.RowOrigin) / SurfaceElementVM.RowHeight) + 1;

        var last = 0;
        foreach (var element in Elements)
        {
            if (element.Element.Row > last)
                last = element.Element.Row;
        }

        return Math.Clamp(band, 1, last + 1);
    }

    /// <summary>How many cells of a row the pointer is to the right of.</summary>
    /// <remarks>
    /// Measured against the midpoint, not the edges: the author is choosing which cell to sit beside,
    /// so crossing a cell's middle is the moment the choice becomes that cell's other side. The cell
    /// being dragged is left out — it is about to leave this row, and counting it would make a drop on
    /// its own place read as a move past it.
    /// </remarks>
    private int InsertionIndex(int row, double canvasX)
    {
        var index = 0;

        foreach (var element in Elements)
        {
            if (element.Element.Row != row || ReferenceEquals(element, _reordering))
                continue;

            if (canvasX > element.Left + (element.Width / 2))
                index++;
        }

        return index;
    }

    /// <summary>Where the drop line is drawn for a landing at one place in a row.</summary>
    private double LineLeft(int row, int index)
    {
        var cells = Elements
            .Where(e => e.Element.Row == row && !ReferenceEquals(e, _reordering))
            .ToList();

        // No cell to aim between: an empty row, or the new row below the last one. The line goes to
        // the inset where that row's first cell would start, which is also where the drop lands.
        if (cells.Count == 0)
            return SurfaceElementVM.CellGap / 2;

        if (index <= 0)
            return Math.Max(SurfaceElementVM.CellGap / 2, cells[0].Left - (SurfaceElementVM.CellGap / 2));

        if (index >= cells.Count)
            return Math.Min(
                CanvasWidth - (SurfaceElementVM.CellGap / 2),
                cells[^1].Right + (SurfaceElementVM.CellGap / 2));

        return (cells[index - 1].Right + cells[index].Left) / 2;
    }

    /// <summary>The top edge a row's cells have, whether or not anything is on that row.</summary>
    private static double RowTop(int row) =>
        SurfaceElementVM.RowOrigin + ((row - 1) * SurfaceElementVM.RowHeight) + (SurfaceElementVM.CellGap / 2);

    /// <summary>
    /// Writes the canvas and reports what the validator would drop — in that order, always.
    /// </summary>
    /// <remarks>
    /// <para><b>The write comes first, and nothing below can undo it.</b> The canvas is the only copy
    /// of the author's layout between saves, so a save that refuses to run because the card currently
    /// has a problem throws that work away over a key they can re-declare. The user's rule is the
    /// whole design here: <em>saving must never be blocked by a warning</em>. Writing before asking
    /// is what makes that structural rather than a promise — even a validator that threw could not
    /// take the file with it.</para>
    ///
    /// <para><b>The document written is the one on the canvas, not the one that survives
    /// validation.</b> Pruning before writing would delete the very elements the warning is about,
    /// and the author would be told about cells that are already gone. So the file keeps everything,
    /// and <see cref="ValidationMessage"/> names what a render will leave out.</para>
    ///
    /// <para><b>Diagnosis is delegated, not re-implemented.</b> The old version hand-checked
    /// <c>key</c> against <see cref="DeclaredKeys"/> here and knew about exactly one rule. The
    /// validator knows a dozen — <c>key-ignored</c>, <c>inert</c>, bad weight, unknown emphasis,
    /// list key not an array — and lives inside <c>KitX.ToolKit</c>, which this assembly cannot see;
    /// <see cref="ISurfaceStore.Diagnose"/> is the seam that lets it be asked.</para>
    /// </remarks>
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(ToolkitId))
            return;

        var document = new SurfaceDocument { Elements = CurrentElements() };

        _store.Save(ToolkitId, document);

        // Materialised rather than passed live: the store is handed a question, not a window onto a
        // collection the user can still edit.
        var warnings = _store.Diagnose(document, [.. DeclaredKeys]);

        // The panel describes this save, not the previous one.
        ValidationMessage = null;
        Diagnostics.Clear();

        if (warnings.Count > 0)
        {
            foreach (var warning in warnings)
                Diagnostics.Add(warning);

            Status = TranslateText("Bench_Surface_SavedWithProblems") ?? "Saved, with problems.";

            Log.Warning("[SurfaceEditor] '{Toolkit}' was saved with {Count} diagnostic(s)",
                ToolkitId, warnings.Count);
        }
        else
        {
            Status = TranslateText("Bench_Surface_Saved") ?? "Saved.";
        }

        RefreshUnplaced();

        // Only after the write returned: a save that threw left the file as it was, and the canvas
        // is still the only copy of the author's work.
        IsDirty = false;
    }

    /// <summary>How often the editor writes the canvas while it is open.</summary>
    /// <remarks>
    /// Thirty seconds is S10's number, and it is a compromise about interruption rather than about
    /// performance: frequent enough that a crash loses at most half a minute of laying out, rare
    /// enough that a writer without a visible action does not look like an editor that keeps doing
    /// things on its own.
    /// </remarks>
    public TimeSpan AutoSaveInterval => TimeSpan.FromSeconds(30);

    /// <summary>Whether the last automatic write failed, so a later success can take the alarm down.</summary>
    private bool _autoSaveFailed;

    /// <summary>Writes the canvas if it has changed, without validating it.</summary>
    /// <remarks>
    /// <para><b>The clock's whole job.</b> Called every <see cref="AutoSaveInterval"/> by the window;
    /// silent when the card is clean, so an author who is reading rather than editing sees nothing
    /// happen.</para>
    ///
    /// <para><b>No validation.</b> S10 is explicit about this, and the reason is the panel: a check
    /// every thirty seconds would either replace what the author is reading with a fresh verdict or
    /// pop a box over the canvas — both make the editor unusable for the one activity it exists for.
    /// What a render would drop is a question worth asking on purpose, which is what "Save and check"
    /// and closing the window are for.</para>
    ///
    /// <para><b>A failed write is a different kind of news.</b> A card that cannot reach the disk is
    /// losing work while the author keeps laying out, so this is the one automatic action that speaks:
    /// the status line carries the reason, and the check panel carries it as a finding, because that is
    /// the only list in the window that survives long enough to be read.</para>
    /// </remarks>
    public void AutoSaveIfDirty()
    {
        if (!IsDirty || string.IsNullOrWhiteSpace(ToolkitId))
            return;

        var document = new SurfaceDocument { Elements = CurrentElements() };

        try
        {
            _store.Save(ToolkitId, document);
        }
        catch (Exception ex)
        {
            ReportSaveFailure(ex);
            return;
        }

        IsDirty = false;
        ClearSaveFailure();
    }

    /// <summary>The code an automatic write failure is reported under.</summary>
    public const string SaveFailedCode = "surface.save.failed";

    /// <summary>Writes the card before the window closes, then asks the validator what a render would drop.</summary>
    /// <returns>What the window has to do about it.</returns>
    /// <remarks>
    /// <para><b>Write first, always.</b> The user's rule from 2026-11-13 is that a warning must never
    /// cost the file — so the write happens before the validator is asked anything, and a card full of
    /// findings still lands on disk. The findings are then reported, because leaving the editor is the
    /// last moment at which saying them is useful.</para>
    ///
    /// <para><b>A write that failed keeps the window open.</b> That is the one case where closing
    /// silently destroys work: the file is not the canvas, and this method cannot promise the author
    /// that anything is saved. Validation is not even attempted — there is nothing to hand it.</para>
    /// </remarks>
    public SurfaceCloseVerdict PrepareToClose()
    {
        if (string.IsNullOrWhiteSpace(ToolkitId))
            return SurfaceCloseVerdict.Close;

        var document = new SurfaceDocument { Elements = CurrentElements() };

        if (IsDirty)
        {
            try
            {
                _store.Save(ToolkitId, document);
            }
            catch (Exception ex)
            {
                ReportSaveFailure(ex);
                return SurfaceCloseVerdict.WriteFailed;
            }

            IsDirty = false;
            ClearSaveFailure();
        }

        var warnings = _store.Diagnose(document, [.. DeclaredKeys]);

        ValidationMessage = null;
        Diagnostics.Clear();

        foreach (var warning in warnings)
            Diagnostics.Add(warning);

        Status = warnings.Count > 0
            ? TranslateText("Bench_Surface_SavedWithProblems") ?? "Saved, with problems."
            : TranslateText("Bench_Surface_Saved") ?? "Saved.";

        return warnings.Count > 0 ? SurfaceCloseVerdict.ConfirmFindings : SurfaceCloseVerdict.Close;
    }

    /// <summary>Takes down the alarm raised by an earlier failed write, now that a write succeeded.</summary>
    /// <remarks>
    /// Status is emptied only when the alarm was ours: a "Saved." from the button is not something a
    /// background write is allowed to erase, and a stale failure left standing on a card that has just
    /// been written is an accusation the author cannot act on.
    /// </remarks>
    private void ClearSaveFailure()
    {
        if (!_autoSaveFailed)
            return;

        _autoSaveFailed = false;
        Status = string.Empty;

        // Walked backwards, because removing the entry the loop is about to look at would shift the
        // one it has not seen yet.
        for (var i = Diagnostics.Count - 1; i >= 0; i--)
        {
            if (Diagnostics[i].Code == SaveFailedCode)
                Diagnostics.RemoveAt(i);
        }
    }

    /// <summary>Makes a failed write visible in both places the author can still see.</summary>
    private void ReportSaveFailure(Exception exception)
    {
        _autoSaveFailed = true;

        // The resource takes the reason as {0} and already exists (the Bench window says the same
        // thing); the diagnostics panel is the only place that will still be holding it a minute
        // later, which is why the failure is written into both.
        var message = string.Format(
            TranslateText("Bench_SaveFailed") ?? "Save failed: {0}",
            exception.Message);

        Status = message;

        Diagnostics.Clear();
        Diagnostics.Add(Diagnostic.Error(SaveFailedCode, message));

        Log.Error(exception, "[SurfaceEditor] '{Toolkit}' could not be written", ToolkitId);
    }

    /// <summary>What the window has to do after <see cref="PrepareToClose"/>.</summary>
    public enum SurfaceCloseVerdict
    {
        /// <summary>Nothing left to decide: the card is on disk and the validator has no findings.</summary>
        Close,

        /// <summary>The card is on disk, and a render would drop something — ask before leaving.</summary>
        ConfirmFindings,

        /// <summary>The card could not be written, so closing would lose it — stay open.</summary>
        WriteFailed,
    }

    /// <summary>Removes every element from the canvas, without touching the file until saved.</summary>
    public void Clear()
    {
        Elements.Clear();
        Boundaries.Clear();
        Select(null);
        ForgetLastSave();
        Status = string.Empty;
        RefreshUnplaced();
        MarkDirty();
    }

    /// <summary>Puts a declared key on the canvas as the element its type implies.</summary>
    /// <param name="key">The key to place.</param>
    public void Place(KeyDeclaration key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Elements.Count >= MaxElements)
        {
            // string.Format, because the resource text carries a {0} for the limit: the two halves
            // of this line used to disagree, the fallback interpolating the number into a literal
            // while the resource would have been returned unformatted, braces and all. Same call
            // shape as every other translation in the Bench canvas.
            ValidationMessage = string.Format(
                TranslateText("Bench_Surface_TooManyElements")
                    ?? "A card holds at most {0} elements.",
                MaxElements);
            return;
        }

        // Built on the list that will be on screen, not on Elements.Count: the new element is about
        // to be appended, and its row has to follow the last one rather than repeat it.
        var appended = CurrentElements();
        appended.Add(CreateElementFor(key, NextRow(appended)));
        Rebuild(appended);
        RefreshUnplaced();
        MarkDirty();

        // Selected on arrival, because the next thing the author wants is almost always a property of
        // the thing they just placed — and before this the only properties reachable were none.
        Select(Elements.Count > 0 ? Elements[^1] : null);
    }

    /// <summary>Puts one of the interactive kinds on the canvas, on a row of its own.</summary>
    /// <param name="kind">One of <see cref="PaletteKinds"/>.</param>
    /// <remarks>
    /// <b>Named on arrival, and the name is the kind.</b> The validator drops an editing element that
    /// neither names itself nor sends anything (<c>surface.element.inert</c>), so a bare <c>Input</c>
    /// would disappear the next time the card was opened — the editor handing out elements that its own
    /// check panel condemns. The name is a placeholder the author is expected to replace, and it is
    /// visible in the cell rather than hidden in a default.
    /// </remarks>
    public void AddKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return;

        if (Elements.Count >= MaxElements)
        {
            ValidationMessage = string.Format(
                TranslateText("Bench_Surface_TooManyElements") ?? "A card holds at most {0} elements.",
                MaxElements);
            return;
        }

        SurfaceElement? created = kind switch
        {
            "Button" => new SurfaceButtonElement { Label = "Button" },
            "Input" => new SurfaceInputElement { Name = "input" },
            "Number" => new SurfaceNumberElement { Name = "number" },
            "Select" => new SurfaceSelectElement { Name = "select" },
            "Switch" => new SurfaceSwitchElement { Name = "switch" },
            _ => null,
        };

        if (created is null)
            return;

        var document = CurrentElements();

        // A row of its own, exactly like a placed key: combining two elements is the author's act, and
        // it is one field away (the row), so the editor does not guess a pairing.
        document.Add(created with { Row = NextRow(document) });

        Rebuild(document);
        RefreshUnplaced();
        MarkDirty();
        Select(Elements.Count > 0 ? Elements[^1] : null);
    }

    /// <summary>Removes one element from the canvas.</summary>
    /// <param name="element">The element to remove.</param>
    public void Remove(SurfaceElementVM element)
    {
        if (element is null)
            return;

        // Rebuilt rather than removed in place: the survivors on that row get wider, and their
        // views were built against a document that still contained this one.
        var remaining = CurrentElements();
        remaining.Remove(element.Element);
        Rebuild(remaining);
        ValidationMessage = null;
        RefreshUnplaced();
        MarkDirty();
    }

    /// <summary>Moves one element to a different row of the card.</summary>
    /// <param name="element">The element to move.</param>
    /// <param name="row">The row number it should sit on. One or more.</param>
    /// <remarks>
    /// <para><b>Kept although nothing calls it yet.</b> S4 makes "move to another row" a second
    /// gesture — dragging the cell itself, as distinct from dragging the border beside it — and this
    /// method is where that gesture lands. Deleting it because the spinner that used to call it is
    /// gone would mean writing the "insert after the last element already on the target row" rule
    /// again later.</para>
    ///
    /// <para>Moving onto an occupied row puts the arrival at the end of that row rather than at the
    /// end of the document: the element order is also the left-to-right order of a row (Q101), so a
    /// new arrival belongs after whatever was already there.</para>
    /// </remarks>
    public void MoveToRow(SurfaceElementVM element, int row)
    {
        if (element is null || row < 1)
        {
            ValidationMessage = TranslateText("Bench_Surface_RowOutOfRange") ?? "Row must be at least 1.";
            return;
        }

        var index = Elements.IndexOf(element);

        if (index < 0)
            return;

        if (element.Element.Row == row)
        {
            ValidationMessage = null;
            return;
        }

        // Reordered in the collection rather than rebuilt. A rebuild replaces every cell's view model,
        // and the row is typed into a field that is bound to the selected cell — so typing "12" would
        // commit "1", rebuild, and lose the focus before the "2" arrived. Moving the same objects keeps
        // the inspector, and the field the cursor is in, exactly where they are.
        Elements.RemoveAt(index);

        // After the last element already on that row, so it lands on the right-hand end of it.
        var lastOnRow = -1;
        for (var i = 0; i < Elements.Count; i++)
        {
            if (Elements[i].Element.Row == row)
                lastOnRow = i;
        }

        var insertAt = lastOnRow < 0 ? Elements.Count : lastOnRow + 1;
        Elements.Insert(insertAt, element);

        var document = CurrentElements();
        document[insertAt] = document[insertAt] with { Row = row };

        // Every cell is refreshed, not only the two rows involved: a row's cells share their width, so
        // the cells the element joined and the cells it left both changed size, and their order in the
        // collection decides left-to-right placement.
        for (var i = 0; i < Elements.Count; i++)
            Elements[i].Refresh(document[i], document);

        RebuildBoundaries();
        ForgetLastSave();
        ValidationMessage = null;
        MarkDirty();
    }

    /// <summary>How many elements a card may hold. A guard against an accidental infinite scroll.</summary>
    public const int MaxElements = 200;

    /// <summary>The row a newly placed element goes on: below the lowest one in use.</summary>
    /// <remarks>
    /// Below the lowest row, not below the element count. Two elements on one row make those two
    /// numbers disagree, and the count would then leave an empty row between the card's content and
    /// whatever was placed next.
    /// </remarks>
    private static int NextRow(IReadOnlyList<SurfaceElement> document) =>
        document.Count == 0 ? 1 : document.Max(e => e.Row) + 1;

    /// <summary>Rebuilds the inspector for the selected element, from the element itself.</summary>
    /// <remarks>
    /// The field list is generated from the element's kind rather than declared per kind in XAML: the
    /// contract already says which properties each kind has, and a second copy of that table in the
    /// view is a second thing to forget. What the inspector offers is therefore exactly what the file
    /// can hold.
    /// </remarks>
    private void BuildFields()
    {
        Fields.Clear();

        if (_selected is null)
            return;

        // The row first, because every kind has one, and because it is the only field that is a
        // statement about the document rather than about the element.
        Fields.Add(new SurfaceFieldVM(
            "row",
            _selected.Element.Row.ToString(CultureInfo.InvariantCulture),
            MoveSelectedToRow));

        switch (_selected.Element)
        {
            case SurfaceTextElement:
                AddField<SurfaceTextElement>("key", e => e.Key, (e, v) => e with { Key = Null(v) }, KeyChoices());
                AddField<SurfaceTextElement>("format", e => e.Format, (e, v) => e with { Format = Null(v) },
                    ["", "text", "date", "datetime", "percent"]);
                break;

            case SurfaceButtonElement:
                AddField<SurfaceButtonElement>("label", e => e.Label, (e, v) => e with { Label = v });
                AddField<SurfaceButtonElement>("events", e => Join(e.Events), (e, v) => e with { Events = Split(v) });
                AddField<SurfaceButtonElement>("emphasis", e => e.Emphasis, (e, v) => e with { Emphasis = Null(v) },
                    ["", "primary", "danger"]);
                break;

            case SurfaceInputElement:
                AddField<SurfaceInputElement>("name", e => e.Name, (e, v) => e with { Name = Null(v) });
                AddField<SurfaceInputElement>("events", e => Join(e.Events), (e, v) => e with { Events = Split(v) });
                break;

            case SurfaceNumberElement:
                AddField<SurfaceNumberElement>("name", e => e.Name, (e, v) => e with { Name = Null(v) });
                AddNumberField<SurfaceNumberElement>("min", e => e.Min, (e, n) => e with { Min = n });
                AddNumberField<SurfaceNumberElement>("max", e => e.Max, (e, n) => e with { Max = n });
                AddNumberField<SurfaceNumberElement>("step", e => e.Step, (e, n) => e with { Step = n });
                AddNumberField<SurfaceNumberElement>(
                    "decimals",
                    e => e.Decimals is null ? null : e.Decimals.Value,
                    (e, n) => e with { Decimals = n is null ? null : (int)n.Value });
                break;

            case SurfaceSelectElement:
                AddField<SurfaceSelectElement>("name", e => e.Name, (e, v) => e with { Name = Null(v) });
                AddField<SurfaceSelectElement>("options", e => Join(e.Options), (e, v) => e with { Options = Split(v) });
                AddField<SurfaceSelectElement>("optionsKey", e => e.OptionsKey, (e, v) => e with { OptionsKey = Null(v) },
                    KeyChoices());
                break;

            case SurfaceSwitchElement:
                AddField<SurfaceSwitchElement>("name", e => e.Name, (e, v) => e with { Name = Null(v) });
                break;

            case SurfaceListElement:
                AddField<SurfaceListElement>("key", e => e.Key, (e, v) => e with { Key = Null(v) }, KeyChoices());
                AddField<SurfaceListElement>("itemTitle", e => e.ItemTitle, (e, v) => e with { ItemTitle = Null(v) });
                AddField<SurfaceListElement>("itemSubtitle", e => e.ItemSubtitle, (e, v) => e with { ItemSubtitle = Null(v) });
                break;
        }
    }

    /// <summary>Adds one field, reading from and writing to the selected element's current value.</summary>
    /// <remarks>
    /// Both delegates take the element as it is <em>now</em> rather than the one the field was built
    /// from: properties are edited one after another, and a writer closed over the first element would
    /// undo every edit made before it.
    /// </remarks>
    private void AddField<T>(
        string label,
        Func<T, string?> read,
        Func<T, string, SurfaceElement> write,
        IReadOnlyList<string>? choices = null)
        where T : SurfaceElement
    {
        if (_selected?.Element is not T current)
            return;

        Fields.Add(new SurfaceFieldVM(
            label,
            read(current),
            text =>
            {
                if (_selected?.Element is T now)
                    ApplyEdit(write(now, text));
            },
            choices));
    }

    /// <summary>Adds a field holding one optional number, refusing what is not one.</summary>
    private void AddNumberField<T>(
        string label,
        Func<T, double?> read,
        Func<T, double?, SurfaceElement> write)
        where T : SurfaceElement
        => AddField<T>(
            label,
            e => read(e)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            (e, text) =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return write(e, null);

                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    // Refused, and said out loud: a silently ignored keystroke is indistinguishable
                    // from a field that does not work.
                    ValidationMessage = $"'{label}' must be a number.";
                    return e;
                }

                return write(e, number);
            });

    /// <summary>Writes one property into the document and redraws what it affected.</summary>
    private void ApplyEdit(SurfaceElement updated)
    {
        if (_selected is null)
            return;

        var index = Elements.IndexOf(_selected);
        if (index < 0)
            return;

        var document = CurrentElements();

        // A refused edit comes back as the element it started from. Nothing to write, and — more to
        // the point — nothing to forget: the panel must keep saying what the refusal said.
        if (ReferenceEquals(document[index], updated))
            return;

        document[index] = updated;

        // A cell's width is a share of its row and no property here changes a weight, so refreshing
        // the row is enough: the other rows cannot have moved.
        for (var i = 0; i < Elements.Count; i++)
        {
            if (document[i].Row == updated.Row)
                Elements[i].Refresh(document[i], document);
        }

        ForgetLastSave();
        MarkDirty();
    }

    /// <summary>Moves the selected cell to a row typed into the inspector.</summary>
    private void MoveSelectedToRow(string text)
    {
        if (_selected is null)
            return;

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) || row < 1)
        {
            ValidationMessage = TranslateText("Bench_Surface_RowOutOfRange") ?? "Row must be at least 1.";
            return;
        }

        MoveToRow(_selected, row);
    }

    /// <summary>The choices for a property that names a declared key, with "none" first.</summary>
    private IReadOnlyList<string> KeyChoices() =>
        ["", .. DeclaredKeys.Select(k => k.Name).Distinct(StringComparer.Ordinal)];

    /// <summary>Comma-separated text from a list, and back.</summary>
    /// <remarks>
    /// The file holds a list; a person types a line. Joining and splitting is the whole translation,
    /// and it is lossless because neither an event name nor an option can contain a comma.
    /// </remarks>
    private static string Join(IReadOnlyList<string> values) => string.Join(", ", values);

    private static string[] Split(string text) =>
        [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Text as an optional property: blank is absent.</summary>
    private static string? Null(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// Builds the element a declared key implies.
    /// </summary>
    /// <remarks>
    /// <para><b>A declared key is a stored value, so what it places is a display element.</b> The
    /// palette lists <c>KeyDeclaration</c>s — things the ToolKit's workflows write — and the only
    /// kinds that show a stored value are <see cref="SurfaceTextElement"/> and
    /// <see cref="SurfaceListElement"/>. The four editing kinds
    /// (<c>Input</c>/<c>Number</c>/<c>Select</c>/<c>Switch</c>) show what the person typed, carry no
    /// key at all (Q88), and cannot be reached from a list of keys.</para>
    ///
    /// <para><b>Two of the four mappings used to break that, and both were measured.</b> A
    /// <c>number</c> key became <c>NumberElement { Key = … }</c> and a <c>boolean</c> key became
    /// <c>SwitchElement { Key = … }</c> — editing kinds bound to keys. Two consequences, neither
    /// reachable in the product and both real:</para>
    ///
    /// <code>
    /// SurfaceValidator.Validate on such a document:
    ///   number  → kept=0  [surface.element.key-ignored, surface.element.inert]
    ///   boolean → kept=0  [surface.element.key-ignored, surface.element.inert]
    ///
    /// SurfaceCard.SetValue on such a cell:
    ///   number  → NotSupportedException: no way to show a value in NumericUpDown
    /// </code>
    ///
    /// <para>Nothing in the product ran either path: <c>SurfaceValidator</c> has no production call
    /// site, so nothing dropped the element, and a <c>Number</c>'s value is written by a workflow, so
    /// nothing pushed one into it. Two missing wires hid one broken author.</para>
    ///
    /// <para><b>Fixing this one mapping does not close the road.</b> A document reaches the renderer
    /// from two places, and neither is checked: <c>SurfaceStore.Load</c> deserialises whatever is on
    /// disk, and <c>ViewSession.Attach</c> logs the surface validator's diagnostics and attaches
    /// anyway. Both still carry a hand-written <c>Number { Key = "x" }</c> to the same
    /// <c>NotSupportedException</c>. The intercept for those is <c>SurfaceValidator</c>, which has no
    /// production call site -- so what this fixes is the producer that could be fixed without wiring
    /// anything, not the shape.</para>
    ///
    /// <para><b>A type this build does not know also gets <see cref="SurfaceTextElement"/> rather
    /// than being refused.</b> The type is advisory, and an author should be able to lay out a key
    /// whose type they intend to change.</para>
    ///
    /// <para><b>One key per row.</b> Before Q101 this packed four keys to a row because there were
    /// four columns to fill; now a row is a group the author composes, and the palette has no
    /// opinion about which keys belong together. A newly placed key getting the whole row is the
    /// honest default — it says "nothing else is on this row yet" — and combining rows is then an
    /// act the author performs rather than one the editor guessed.</para>
    ///
    /// <para>The weight is left at its default of 1, which alone on a row means the full width.
    /// Setting it here would record a ratio that means nothing until a second element joins.</para>
    /// </remarks>
    private static SurfaceElement CreateElementFor(KeyDeclaration key, int row)
    {
        // Two kinds, not four. Text draws every scalar (numbers and booleans reach the screen as
        // their JSON text, which is what Scalars does); List is the only kind that can draw an array.
        // A boolean key shown as a Switch would be an input control where a stored value belongs --
        // the person cannot tick their way into changing what a workflow wrote.
        SurfaceElement element = key.Type switch
        {
            "array" => new SurfaceListElement { Key = key.Name },
            _ => new SurfaceTextElement { Key = key.Name },
        };

        return element with { Row = row };
    }

    private void RefreshUnplaced()
    {
        var placed = Elements
            .Where(e => e.Key is not null)
            .Select(e => e.Key!)
            .ToHashSet(StringComparer.Ordinal);
        UnplacedKeys.Clear();
        foreach (var key in DeclaredKeys.Where(k => !placed.Contains(k.Name)))
            UnplacedKeys.Add(key);
    }

    /// <inheritdoc/>
    public override void InitCommands()
    {
        PlaceCommand = ReactiveCommand.Create<KeyDeclaration>(Place);
        AddKindCommand = ReactiveCommand.Create<string>(AddKind);
        SelectCommand = ReactiveCommand.Create<SurfaceElementVM?>(Select);
        RemoveCommand = ReactiveCommand.Create<SurfaceElementVM>(Remove);
        SaveCommand = ReactiveCommand.Create(Save);
        ClearCommand = ReactiveCommand.Create(Clear);
    }

    /// <inheritdoc/>
    public override void InitEvents()
    {
        // Nothing to wire: the editor owns no events of its own, and the store's Changed event is
        // for a live View, not for the window that just wrote the file.
    }

    /// <summary>Puts a declared key on the canvas.</summary>
    public ReactiveCommand<KeyDeclaration, Unit> PlaceCommand { get; private set; } = null!;

    /// <summary>Puts an interactive element on the canvas.</summary>
    public ReactiveCommand<string, Unit> AddKindCommand { get; private set; } = null!;

    /// <summary>Selects the cell the inspector describes.</summary>
    public ReactiveCommand<SurfaceElementVM?, Unit> SelectCommand { get; private set; } = null!;

    /// <summary>Removes an element from the canvas.</summary>
    public ReactiveCommand<SurfaceElementVM, Unit> RemoveCommand { get; private set; } = null!;

    /// <summary>Writes the canvas to the store.</summary>
    public ReactiveCommand<Unit, Unit> SaveCommand { get; private set; } = null!;

    /// <summary>Empties the canvas in memory.</summary>
    public ReactiveCommand<Unit, Unit> ClearCommand { get; private set; } = null!;
}
