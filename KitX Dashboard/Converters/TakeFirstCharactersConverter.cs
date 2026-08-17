using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Returns the first N characters of a string. N comes from ConverterParameter, which
/// accepts either an int, a numeric string ("8"), or "N:ellipsis" ("28:...") where any
/// non-empty suffix after ':' appends an ellipsis (" ...") when the string is truncated.
/// The bare numeric form truncates without an ellipsis. Defaults to 8 when unparseable.
/// </summary>
public sealed class TakeFirstCharactersConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
            return null;

        var (count, appendEllipsis) = Parse(parameter);
        if (count <= 0) return null;
        if (count >= text.Length) return text;

        return appendEllipsis
            ? string.Concat(text.AsSpan(0, count), " ...")
            : text[..count];
    }

    private static (int Count, bool AppendEllipsis) Parse(object? parameter)
    {
        switch (parameter)
        {
            case int n:
                return (n, false);
            case string s when s.Length > 0:
                var colon = s.IndexOf(':');
                if (colon >= 0)
                {
                    if (int.TryParse(s[..colon], out var prefixedCount))
                        return (prefixedCount, s[(colon + 1)..].Length > 0);
                }
                else if (int.TryParse(s, out var bareCount))
                {
                    return (bareCount, false);
                }
                return (8, false);
            default:
                return (8, false);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
