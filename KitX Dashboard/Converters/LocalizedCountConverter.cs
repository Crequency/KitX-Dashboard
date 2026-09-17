using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Formats a count with a localized "{0} ..." format string. The converter parameter is the
/// resource-key suffix (e.g. "WorkflowCount"), resolved as <c>Text_ToolKit_{suffix}</c> via
/// <see cref="ViewModelBase.TranslateTextWithSuffix"/>. Used for the ToolKit card counts
/// (workflows / triggers / plugin requirements).
/// </summary>
public sealed class LocalizedCountConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var key = parameter as string ?? string.Empty;
        var format = ViewModelBase.TranslateTextWithSuffix("ToolKit", key) ?? $"{{0}} {key}";
        return string.Format(format, count);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
