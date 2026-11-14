using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// One editable property of the selected element.
/// </summary>
/// <remarks>
/// <para><b>The label is the field's name in the file, not a translation of it.</b> This window writes
/// <c>surface.json</c>, and the person using it is one JSON edit away from never opening it again. A
/// label that says <c>events</c> teaches the file; a label that says "事件" forces every question about
/// the card back through this window. It is also the only label that cannot drift: the contract's
/// property name is what the field writes.</para>
///
/// <para><b>Two kinds of editor, not seven.</b> A property is either free text or one of a fixed set.
/// Everything else — a number, a date, a list of event names — is free text whose <em>apply</em>
/// decides whether it is acceptable, and says so through the editor's one refusal channel. A field
/// type per property would put the validation in the wrong place: it belongs beside the element it
/// edits, not in a list of editors.</para>
///
/// <para><b>The value is applied as it is typed, with no confirm step.</b> The canvas has no
/// "entered but not applied" state anywhere else either — a drag ends where the pointer is released,
/// and that end is the committed value. A confirm button here would be the same two-step model that
/// was removed from the weight editor, reintroduced for the properties beside it.</para>
/// </remarks>
public sealed class SurfaceFieldVM : INotifyPropertyChanged
{
    private readonly Action<string> _apply;
    private string _value;

    /// <summary>Creates a field.</summary>
    /// <param name="label">The property name from the contract, shown as-is.</param>
    /// <param name="value">What it currently holds, as text. Null becomes the empty string.</param>
    /// <param name="apply">What to do with a new value. Called on every change.</param>
    /// <param name="choices">The fixed set, for a property that has one. Empty means free text.</param>
    public SurfaceFieldVM(string label, string? value, Action<string> apply, IReadOnlyList<string>? choices = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(apply);

        Label = label;
        _value = value ?? string.Empty;
        _apply = apply;
        Choices = choices ?? [];
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The property's name in the contract.</summary>
    public string Label { get; }

    /// <summary>What the property currently holds, as text.</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (string.Equals(_value, value, StringComparison.Ordinal))
                return;

            _value = value ?? string.Empty;
            Raise(nameof(Value));

            // Applied after the notification, so a handler that rebuilds this list (a row change
            // reshuffles the document) does so once the bindings have been told what happened.
            _apply(_value);
        }
    }

    /// <summary>The fixed set this property may take, or empty when it is free text.</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>Whether this field is picked rather than typed.</summary>
    public bool IsChoice => Choices.Count > 0;

    /// <summary>Whether this field is typed.</summary>
    public bool IsText => !IsChoice;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
