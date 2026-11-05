using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Renders an instance's run counters as "<c>completed / failed</c> runs", localized.
/// </summary>
/// <remarks>
/// <para>Replaces the pre-S4 <c>{Binding RunSummary}</c>. That property lived on
/// <see cref="InstanceSnapshot"/> and formatted its numbers with a format string from the
/// implementation assembly (a concrete type, not a contract one) — which is why it had to be
/// removed at S0 for the snapshot to live in the contract at all. The numbers it formatted were
/// already on the snapshot; only the sentence was missing, and a sentence belongs to the View.</para>
/// <para>Takes the whole row as the value so both counters are reachable in one pass. Returns an
/// empty string rather than null for anything unexpected, so a malformed row shows no line
/// instead of an exception in the binding pipeline.</para>
/// </remarks>
public sealed class InstanceRunsSummaryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not InstanceSnapshot snapshot)
            return string.Empty;

        var format = ViewModelBase.Translate("Text_PanelHost_RunSummaryDetail")
                     ?? "{0} completed · {1} failed";

        return string.Format(format, snapshot.CompletedRuns, snapshot.FailedRuns);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
