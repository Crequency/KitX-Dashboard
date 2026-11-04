using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Renders an instance's <see cref="InstanceSnapshot.Outcome"/> as a localized word
/// (running / succeeded / failed / cancelled).
/// </summary>
/// <remarks>
/// <para>Replaces the raw <c>{Binding Status}</c> text the pre-S4 XAML showed. The enum was
/// rendered by its own member names, which meant the panel displayed <c>Running</c> in every
/// language. Formatting a verdict is a View concern (ADR-0012 D3 puts presentation outside the
/// contract), so the words live in the resource files and this converter picks between them.</para>
/// </remarks>
public sealed class InstanceOutcomeToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => Translate("Text_PanelHost_InstanceRunning"),
            RunOutcome.Succeeded => Translate("Text_PanelHost_InstanceSucceeded"),
            RunOutcome.Failed => Translate("Text_PanelHost_InstanceFailed"),
            RunOutcome.Cancelled => Translate("Text_PanelHost_InstanceCancelled"),
            _ => string.Empty,
        };

    /// <summary>Falls back to the key's tail when a resource is missing, so the row still says
    /// something meaningful instead of going blank.</summary>
    private static string Translate(string key)
        => ViewModelBase.Translate(key) ?? key["Text_PanelHost_Instance".Length..];

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
