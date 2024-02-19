using Avalonia.Data.Converters;
using ExCSS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KitX.Dashboard.Converters;

public class PluginParamTypeVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        return value.ToString() == parameter?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

public class DisplayNamesLanguageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        return value is not Dictionary<string, string> dict
            ? null
            : (object)(dict.TryGetValue(
                 Instances.ConfigManager.AppConfig.App.AppLanguage, out var lang
                ) ? lang : dict.Values.GetEnumerator().Current);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
