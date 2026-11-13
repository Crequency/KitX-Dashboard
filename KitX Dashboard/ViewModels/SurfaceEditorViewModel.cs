using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    private string _toolkitId = string.Empty;
    private string _status = string.Empty;
    private string? _validationMessage;

    /// <summary>Creates the editor.</summary>
    /// <param name="store">Where layouts are read and written.</param>
    /// <param name="keysOf">Supplies a ToolKit's declared keys, by id.</param>
    public SurfaceEditorViewModel(
        ISurfaceStore store,
        Func<string, IReadOnlyList<KeyDeclaration>> keysOf)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(keysOf);

        _store = store;
        _keysOf = keysOf;

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

    /// <summary>The ToolKit being edited.</summary>
    public string ToolkitId
    {
        get => _toolkitId;
        private set => this.RaiseAndSetIfChanged(ref _toolkitId, value);
    }

    /// <summary>What the last save reported.</summary>
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>What the last check refused, or null when it found nothing.</summary>
    /// <remarks>
    /// Kept as a message rather than a boolean because the two things it can say are different
    /// problems: an element whose key is gone, and a save that failed to reach the disk. Collapsing
    /// them into "something is wrong" would leave the reader to guess which.
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

    /// <summary>Loads a ToolKit's current layout.</summary>
    /// <param name="toolkitId">The ToolKit to edit.</param>
    public void Load(string toolkitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitId);

        ToolkitId = toolkitId;
        Status = string.Empty;
        ValidationMessage = null;

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
        var document = elements as IReadOnlyList<SurfaceElement> ?? [.. elements];

        Elements.Clear();
        foreach (var element in document)
            Elements.Add(new SurfaceElementVM(element, document));

        RebuildBoundaries();
    }

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
    }

    /// <summary>Saves the canvas, first checking that every element still refers to a key.</summary>
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(ToolkitId))
            return;

        var problems = new List<string>();

        foreach (var element in Elements)
        {
            if (element.Key is null)
                continue;

            if (!DeclaredKeys.Any(k => string.Equals(k.Name, element.Key, StringComparison.Ordinal)))
                problems.Add($"'{element.Key}' is no longer declared.");
        }
        if (problems.Count > 0)
        {
            // Named, and saved anyway: refusing the save would throw away the author's layout work
            // over a key they can re-add. The check is an alarm, not a gate.
            ValidationMessage = string.Join(" ", problems.Distinct(StringComparer.Ordinal));
            Status = TranslateText("Bench_Surface_SavedWithProblems") ?? "Saved, with problems.";
        }

        _store.Save(ToolkitId, new SurfaceDocument
        {
            Elements = CurrentElements(),
        });

        if (problems.Count == 0)
        {
            ValidationMessage = null;
            Status = TranslateText("Bench_Surface_Saved") ?? "Saved.";
        }

        RefreshUnplaced();
    }

    /// <summary>Removes every element from the canvas, without touching the file until saved.</summary>
    public void Clear()
    {
        Elements.Clear();
        Boundaries.Clear();
        ValidationMessage = null;
        Status = string.Empty;
        RefreshUnplaced();
    }

    /// <summary>Puts a declared key on the canvas as the element its type implies.</summary>
    /// <param name="key">The key to place.</param>
    public void Place(KeyDeclaration key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Elements.Count >= MaxElements)
        {
            ValidationMessage = TranslateText("Bench_Surface_TooManyElements")
                ?? $"A card holds at most {MaxElements} elements.";
            return;
        }

        // Built on the list that will be on screen, not on Elements.Count: the new element is about
        // to be appended, and its row has to follow the last one rather than repeat it.
        var appended = CurrentElements();
        appended.Add(CreateElementFor(key, appended.Count));
        Rebuild(appended);
        RefreshUnplaced();
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

        var moved = CurrentElements();
        var index = moved.IndexOf(element.Element);

        if (index < 0)
            return;

        var updated = moved[index] with { Row = row };

        moved.RemoveAt(index);

        // After the last element already on that row, so it lands on the right-hand end of it.
        var lastOnRow = moved.FindLastIndex(other => other.Row == row);

        moved.Insert(lastOnRow < 0 ? moved.Count : lastOnRow + 1, updated);

        Rebuild(moved);
        ValidationMessage = null;
    }

    /// <summary>How many elements a card may hold. A guard against an accidental infinite scroll.</summary>
    public const int MaxElements = 200;

    /// <summary>
    /// Builds the element a declared key implies.
    /// </summary>
    /// <remarks>
    /// <para>Type drives the choice, because the declaration already says what the View should
    /// draw. A key whose type this build does not know still gets an element —
    /// <see cref="SurfaceTextElement"/> — rather than being refused: the type is advisory, and an
    /// author should be able to lay out a key whose type they intend to change.</para>
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
    private static SurfaceElement CreateElementFor(KeyDeclaration key, int index)
    {
        SurfaceElement element = key.Type switch
        {
            "number" => new SurfaceNumberElement { Key = key.Name },
            "boolean" => new SurfaceSwitchElement { Key = key.Name },
            "array" => new SurfaceListElement { Key = key.Name },
            _ => new SurfaceTextElement { Key = key.Name },
        };

        return element with { Row = index + 1 };
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

    /// <summary>Removes an element from the canvas.</summary>
    public ReactiveCommand<SurfaceElementVM, Unit> RemoveCommand { get; private set; } = null!;

    /// <summary>Writes the canvas to the store.</summary>
    public ReactiveCommand<Unit, Unit> SaveCommand { get; private set; } = null!;

    /// <summary>Empties the canvas in memory.</summary>
    public ReactiveCommand<Unit, Unit> ClearCommand { get; private set; } = null!;
}
