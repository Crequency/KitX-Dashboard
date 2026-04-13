using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KitX.Core.Contract.Workflow;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Converts an IWorkflowCase to a visibility bool based on ConverterParameter state string.
/// Parameter values: "Stopped" (!IsRunning && !IsError), "Running" (IsRunning && !IsError), "Error" (IsError)
/// IsRunning controls buttons (STOP when running), IsError controls light color (yellow when error).
/// </summary>
public class WorkflowStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IWorkflowCase wf || parameter is not string state)
            return false;

        return state switch
        {
            "Stopped" => !wf.IsRunning && !wf.IsError,
            "Running" => wf.IsRunning && !wf.IsError,
            "Error" => wf.IsError,
            _ => false
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
