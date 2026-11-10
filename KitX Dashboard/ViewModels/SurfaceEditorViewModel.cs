using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
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
        InitEvents();
    }

    /// <summary>The keys declared by the ToolKit being edited.</summary>
    public ObservableCollection<KeyDeclaration> DeclaredKeys { get; } = [];

    /// <summary>Keys that are declared but not placed on the canvas yet.</summary>
    public ObservableCollection<KeyDeclaration> UnplacedKeys { get; } = [];

    /// <summary>The elements on the canvas, in document order.</summary>
    /// <remarks>
    /// Wrapped in <see cref="SurfaceElementVM"/> so the canvas gets pixels and a kind label without
    /// the element model learning about either. The wrapped element is what gets saved.
    /// </remarks>
    public ObservableCollection<SurfaceElementVM> Elements { get; } = [];

    /// <summary>The ToolKit being edited.</summary>
    public string ToolkitId
    {
        get => _toolkitId;
        private set => this.RaiseAndSetIfChanged(ref _toolkitId, value);
    }

    /// <summary>A one-line summary of what just happened.</summary>
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>
    /// What "save and check" found, or <see langword="null"/> when it found nothing.
    /// </summary>
    /// <remarks>
    /// Deliberately a message rather than a boolean: the point of the check is to <em>name</em> the
    /// element that refers to a key which no longer exists, so "something is wrong" would leave the
    /// author searching a grid that can be taller than the window.
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

    /// <summary>Whether <see cref="ValidationMessage"/> has anything in it.</summary>
    public bool HasValidationMessage => !string.IsNullOrEmpty(_validationMessage);

    /// <summary>Replaces every view on the canvas from a list of elements.</summary>
    /// <param name="elements">The elements to draw, in declaration order.</param>
    /// <remarks>
    /// <para><b>Every mutation rebuilds the whole canvas rather than patching one cell.</b> A cell's
    /// width is a share of its row (Q101), so adding or removing one element changes the widths of
    /// the others on that row. Patching would leave those views holding a document that no longer
    /// matches the one on screen — and the symptom would be a card whose cells overlap or leave a
    /// gap, which reads as a layout bug rather than as a stale snapshot.</para>
    ///
    /// <para>The cost is a rebuild of at most <see cref="MaxElements"/> small objects, on an action
    /// a person takes by hand.</para>
    /// </remarks>
    private void Rebuild(IReadOnlyList<SurfaceElement> elements)
    {
        Elements.Clear();

        var document = elements as IReadOnlyList<SurfaceElement> ?? [.. elements];
        foreach (var element in document)
            Elements.Add(new SurfaceElementVM(element, document));
    }

    /// <summary>The elements currently on the canvas, in declaration order.</summary>
    private List<SurfaceElement> CurrentElements() =>
        [.. Elements.Select(e => e.Element with { Weight = e.DraftWeight, Row = e.DraftRow })];

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
            Elements = Elements.Select(e => e.Element).ToList(),
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

    /// <summary>Gives one element a different share of its row.</summary>
    /// <param name="element">The element to resize.</param>
    /// <param name="weight">How many parts of the row it should take. One or more.</param>
    /// <remarks>
    /// <para><b>Refused here, tolerated there.</b> A weight below one is not accepted by this method,
    /// while <see cref="SurfaceValidator"/> repairs it to one and reports a warning. The asymmetry is
    /// deliberate: the editor must not let an author write a value that will be reported back at them,
    /// and the loading path must accept whatever somebody else already wrote. Do not "unify" these two
    /// checks — they answer different questions.</para>
    ///
    /// <para>Rebuilt rather than patched, for the same reason <see cref="Remove"/> is: a cell's width
    /// is a share of its row, so changing one weight changes every sibling's width.</para>
    /// </remarks>
    public void SetWeight(SurfaceElementVM element, int weight)
    {
        if (element is null || weight < 1)
        {
            // Refused. This is the only place that says so, because ReactiveUI's CanExecute cannot
            // see the command's parameter and therefore cannot ask "is this cell's draft valid".
            // Staying silent would make the refusal indistinguishable from the button being dead.
            ValidationMessage = TranslateText("Bench_Surface_WeightOutOfRange") ?? "Weight must be at least 1.";
            return;
        }

        var changed = CurrentElements();
        var index = changed.IndexOf(element.Element);

        if (index < 0)
            return;

        changed[index] = changed[index] with { Weight = weight };
        Rebuild(changed);
        ValidationMessage = null;
    }

    /// <summary>Moves one element to a different row of the card.</summary>
    /// <param name="element">The element to move.</param>
    /// <param name="row">The row number it should sit on. One or more.</param>
    /// <remarks>
    /// Moving an element onto an occupied row puts it at the end of that row rather than at the end of
    /// the document — the document's element order is also the left-to-right order of a row (Q101), so
    /// the new arrival belongs after whatever was already there.
    /// </remarks>
    public void MoveToRow(SurfaceElementVM element, int row)
    {
        if (element is null || row < 1)
        {
            // Same reasoning as SetWeight: a silent refusal here reads as a dead button.
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

        // The command takes the cell itself and reads the number off its draft, so a text box binds
        // straight to the property it edits and the parameter needs no packing. The two workhorse
        // methods below still take the number explicitly — they are the ones a test or a later
        // caller would reach for, and neither should have to know that a draft exists.
        //
        // There is no CanExecute here, and that is a limitation rather than a choice: ReactiveUI
        // takes an IObservable<bool>, which cannot see the parameter, so with one command per cell
        // there is nowhere to put "this cell's draft is currently valid". Avalonia still calls
        // Execute, and Execute refuses — so a NumericUpDown that accepted a pasted 0 changes
        // nothing rather than drawing a degenerate row. The guard in SetWeight is therefore the
        // only line of defence and must not be removed as "redundant".
        SetWeightCommand = ReactiveCommand.Create<SurfaceElementVM>(
            element => SetWeight(element, element.DraftWeight));

        MoveToRowCommand = ReactiveCommand.Create<SurfaceElementVM>(
            element => MoveToRow(element, element.DraftRow));
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

    /// <summary>Applies one cell's draft weight to the canvas.</summary>
    public ReactiveCommand<SurfaceElementVM, Unit> SetWeightCommand { get; private set; } = null!;

    /// <summary>Applies one cell's draft row to the canvas.</summary>
    public ReactiveCommand<SurfaceElementVM, Unit> MoveToRowCommand { get; private set; } = null!;
}
