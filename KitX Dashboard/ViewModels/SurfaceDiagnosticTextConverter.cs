using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KitX.ToolKit.Contract;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Binds a row of the check panel to the sentence <see cref="SurfaceDiagnosticText"/> composed for
/// it.
/// </summary>
/// <remarks>
/// <para><b>Why a converter and not a property.</b> The panel's items are the contract's own
/// <see cref="Diagnostic"/> records, straight from the save. Putting the display text on a view
/// model beside each one would mean keeping two collections in step and rebuilding the second
/// whenever the first changed; the sentence is a function of the record, so it is computed where
/// the row is drawn.</para>
///
/// <para><b>What it does not do.</b> It does not cache: a language switch re-renders the panel and
/// the lookup runs again. Holding the text here would freeze the first language it was asked
/// in — the defect that once made every string in this dashboard fall back to English.</para>
/// </remarks>
public sealed class SurfaceDiagnosticTextConverter : IValueConverter
{
    /// <summary>The sentence for a <see cref="Diagnostic"/>, or <see langword="null"/> for anything else.</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Diagnostic diagnostic ? SurfaceDiagnosticText.Describe(diagnostic) : null;

    /// <summary>Not supported: the panel is read-only.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
