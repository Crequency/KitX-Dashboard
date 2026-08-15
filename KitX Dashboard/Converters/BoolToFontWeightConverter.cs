using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Maps a bool to <see cref="FontWeight"/> (SemiBold when true, Normal when false).
/// Used for the panel-host tab buttons — binding a raw bool (or BoolConverters.Not) to
/// FontWeight produced FontWeight=0, which made Avalonia's text layout throw
/// "Font weight must be > 0" and froze the render loop.
/// </summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public static readonly BoolToFontWeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeight.SemiBold : FontWeight.Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FontWeight.SemiBold;
}
