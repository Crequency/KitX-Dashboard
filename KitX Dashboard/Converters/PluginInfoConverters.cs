using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace KitX.Dashboard.Converters;

public class PluginMultiLanguagePropertyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return null;

        if (value is Dictionary<string, string> dict && dict.Count > 0)
        {
            // Current UI language from the app-level static (kept in sync by
            // LanguageLoader) — pure display conversion, no service lookup.
            var appLanguage = App.AppLanguage;
            var result = dict.TryGetValue(appLanguage, out var lang) ? lang : dict.Values.FirstOrDefault() ?? string.Empty;

            return result;
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

public class PluginInfoTagsFetchConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter as string is null)
            return null;

        if (value is Dictionary<string, string> dict)
            if (dict.TryGetValue((parameter as string)!, out var tag))
                return tag;

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

public class PluginInfoSelectedConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 3)
            return false;

        if (values[0] is string id1)
            if (values[1] is string id2)
                return id1.Equals(id2) && (bool)values[2]!;

        return false;
    }
}
