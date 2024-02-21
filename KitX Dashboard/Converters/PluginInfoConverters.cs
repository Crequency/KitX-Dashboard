using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KitX.Dashboard.Converters;

public class PluginMultiLanguagePropertyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        if (value is Dictionary<string, string> dict)
        {
            var result = dict.TryGetValue(
                Instances.ConfigManager.AppConfig.App.AppLanguage,
                out var lang
            ) ? lang : dict.Values.First();

            return result;
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
