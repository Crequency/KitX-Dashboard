using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;
using AvaloniaEdit.Document;
using Common.Activity;

namespace KitX.Dashboard.Converters;

public class AvaloniaEditDocumentStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return null;

        var document = new TextDocument(value as string);

        // D13.7: the parameter (ActivityTaskResultLines) was never consumed — removed
        // the empty branch; keep the parameter on the signature for XAML compatibility.

        return document;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
