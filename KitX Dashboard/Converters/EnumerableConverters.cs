using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace KitX.Dashboard.Converters;

public class GetEnumerableItemByIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is null) return null;

        if (parameter is int index || (parameter is string sindex && int.TryParse(sindex, out index)))
        {
            if (value is IEnumerable values)
                return values.Cast<object>().ElementAtOrDefault(index);
            else if (value is Array array)
                return array.GetValue(index);
            else
                return null;
        }
        else return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
