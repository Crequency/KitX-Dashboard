using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using KitX.ToolKit.Instances;

namespace KitX.Dashboard.Converters;

/// <summary>Maps an <see cref="InstanceStatus"/> to a badge brush (Running = accent, else muted).</summary>
public sealed class InstanceStatusToBrushConverter : IValueConverter
{
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#3873D9"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8A8A8A"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is InstanceStatus.Running ? RunningBrush : MutedBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
