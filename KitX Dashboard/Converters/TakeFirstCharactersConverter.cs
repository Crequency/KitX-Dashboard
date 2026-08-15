using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Returns the first N characters of a string (N from ConverterParameter).
/// Used for the initiator fingerprint tooltip: show the first 8 characters only (UX v2 §5.2).
/// </summary>
public sealed class TakeFirstCharactersConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
            return null;

        var count = parameter switch
        {
            int n => n,
            string s when int.TryParse(s, out var n) => n,
            _ => 8,
        };

        return count > 0 ? (count < text.Length ? text[..count] : text) : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
