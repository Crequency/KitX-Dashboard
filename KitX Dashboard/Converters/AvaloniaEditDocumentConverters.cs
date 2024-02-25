using Avalonia.Data.Converters;
using AvaloniaEdit.Document;
using Common.Activity;
using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace KitX.Dashboard.Converters;

public class AvaloniaEditDocumentStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        var document = new TextDocument(value as string);

        if (parameter is ObservableCollection<ActivityTaskResultLine> lines)
        {

        }

        return document;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
