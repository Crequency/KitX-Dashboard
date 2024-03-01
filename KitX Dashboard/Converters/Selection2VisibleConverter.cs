using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KitX.Dashboard.Converters;

internal class Selection2VisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        return (int)value == 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        return (bool)value ? 0 : 1;
    }
}
