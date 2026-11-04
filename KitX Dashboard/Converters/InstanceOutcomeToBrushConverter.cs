using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using KitX.ToolKit.Contract;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Maps an instance's <see cref="InstanceSnapshot.Outcome"/> to a badge brush: running (= no
/// verdict yet) uses the accent colour, a success is muted, a failure is red and a cancellation
/// is muted.
/// </summary>
/// <remarks>
/// <para>This converter replaced <c>InstanceStatusToBrushConverter</c> at S4, when the
/// <c>InstanceStatus</c> enum was removed (ADR-0005 D1). The enum could only say
/// running/not-running, so the old converter had no way to distinguish "finished fine" from
/// "finished badly" — which is exactly the information a status dot is for.</para>
/// <para><see cref="InstanceSnapshot.Outcome"/> is null while the instance runs, so null is the
/// running case rather than an error.</para>
/// </remarks>
public sealed class InstanceOutcomeToBrushConverter : IValueConverter
{
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#3873D9"));
    private static readonly IBrush SucceededBrush = new SolidColorBrush(Color.Parse("#5A5A5A"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#C42B1C"));
    private static readonly IBrush CancelledBrush = new SolidColorBrush(Color.Parse("#8A8A8A"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => RunningBrush,
            RunOutcome.Succeeded => SucceededBrush,
            RunOutcome.Failed => FailedBrush,
            RunOutcome.Cancelled => CancelledBrush,
            _ => RunningBrush,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
