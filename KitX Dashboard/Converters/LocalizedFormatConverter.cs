using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Formats a value with a localized "{0} ..." format string. The converter parameter is the
/// full resource key (e.g. "Text_PanelHost_RunningCount"), resolved via
/// <see cref="ViewModelBase.Translate"/>. Used for parameterized strings in XAML where the
/// format itself must be localizable (G26).
/// </summary>
public sealed class LocalizedFormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string ?? string.Empty;
        var format = ViewModelBase.Translate(key) ?? "{0}";
        return string.Format(format, value);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
