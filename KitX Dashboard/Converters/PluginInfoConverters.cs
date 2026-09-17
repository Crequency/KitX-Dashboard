using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
            // Current UI language comes from the loaded language resource dictionary
            // (each {lang}.axaml carries a CurrentLanguage key). This converter is
            // instantiated parameterlessly in XAML resources, so it reads the current
            // language from the resource tree instead of a static or a service lookup.
            var appLanguage = CurrentLanguageCode(Application.Current);
            var result = dict.TryGetValue(appLanguage, out var lang) ? lang : dict.Values.FirstOrDefault() ?? string.Empty;

            return result;
        }

        return string.Empty;
    }

    /// <summary>
    /// Resolves the current UI language code from the loaded resource dictionary.
    /// Falls back to <paramref name="fallback"/> when the host is null or the
    /// <c>CurrentLanguage</c> resource is absent (e.g. in headless tests without an
    /// <see cref="Application"/>). Pure display lookup — no service or static dependency.
    /// </summary>
    internal static string CurrentLanguageCode(IResourceHost? host, string fallback = "en-us")
    {
        if (host is not null
            && host.TryFindResource("CurrentLanguage", out var found)
            && found is string lang
            && !string.IsNullOrWhiteSpace(lang))
            return lang;

        return fallback;
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
