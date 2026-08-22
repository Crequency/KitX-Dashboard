using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Converts IsConnected (bool) + ColorHex (string) to a fill brush.
/// Connected: solid fill with the color. Disconnected: transparent.
/// Used as MultiConverter with bindings [IsConnected, ColorHex].
/// </summary>
public class PinFillConverter : IMultiValueConverter
{
    public static readonly PinFillConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return Brushes.Transparent;

        var isConnected = values[0] is bool b && b;
        var colorHex = values[1] as string ?? "#FFFFFF";

        if (!isConnected)
            return Brushes.Transparent;

        try
        {
            var color = Color.Parse(colorHex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Converts IsConnected (bool) + ColorHex (string) to a stroke brush.
/// Connected: no stroke (transparent). Disconnected: stroke with the color.
/// </summary>
public class PinStrokeConverter : IMultiValueConverter
{
    public static readonly PinStrokeConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return Brushes.White;

        var isConnected = values[0] is bool b && b;
        var colorHex = values[1] as string ?? "#FFFFFF";

        if (isConnected)
            return Brushes.Transparent;

        try
        {
            var color = Color.Parse(colorHex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.White;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Converts a hex color string to a SolidColorBrush.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch
            {
                return Brushes.Gray;
            }
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Converts bool (IsExecution) to Visibility for the triangle port shape.
/// </summary>
public class ExecutionPinVisibilityConverter : IValueConverter
{
    public static readonly ExecutionPinVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExec)
            return isExec;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
