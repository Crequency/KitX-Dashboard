using Avalonia.Data.Converters;
using Common.Activity;
using System;
using System.Globalization;
using Material.Icons;
using Avalonia.Media;

namespace KitX.Dashboard.Converters;

public class ActivityStatusIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        if (value is ActivityStatus status)
            switch (status)
            {
                case ActivityStatus.Unknown:
                    return MaterialIconKind.TimerSandEmpty;
                case ActivityStatus.Opened:
                    return MaterialIconKind.TimerSand;
                case ActivityStatus.Pending:
                    return MaterialIconKind.TimerSandPaused;
                case ActivityStatus.Running:
                    return MaterialIconKind.TimerSandComplete;
                case ActivityStatus.Closed:
                    return MaterialIconKind.TimerSandFull;
            }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        if (value is MaterialIconKind kind)
            switch (kind)
            {
                case MaterialIconKind.TimerSandEmpty:
                    return ActivityStatus.Unknown;
                case MaterialIconKind.TimerSand:
                    return ActivityStatus.Opened;
                case MaterialIconKind.TimerSandPaused:
                    return ActivityStatus.Pending;
                case MaterialIconKind.TimerSandComplete:
                    return ActivityStatus.Running;
                case MaterialIconKind.TimerSandFull:
                    return ActivityStatus.Closed;
            }

        return null;
    }
}

public class ActivityTasksStatusIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        if (value is ActivityTaskStatus status)
            switch (status)
            {
                case ActivityTaskStatus.Unknown:
                    return MaterialIconKind.SourceFork;
                case ActivityTaskStatus.Pending:
                    return MaterialIconKind.SourceBranchPlus;
                case ActivityTaskStatus.Running:
                    return MaterialIconKind.SourceBranchSync;
                case ActivityTaskStatus.Success:
                    return MaterialIconKind.SourceBranchCheck;
                case ActivityTaskStatus.Warning:
                    return MaterialIconKind.SourceBranchMinus;
                case ActivityTaskStatus.Errored:
                    return MaterialIconKind.SourceBranchRemove;
            }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        if (value is MaterialIconKind kind)
            switch (kind)
            {
                case MaterialIconKind.SourceFork:
                    return ActivityTaskStatus.Unknown;
                case MaterialIconKind.SourceBranchPlus:
                    return ActivityTaskStatus.Pending;
                case MaterialIconKind.SourceBranchSync:
                    return ActivityTaskStatus.Running;
                case MaterialIconKind.SourceBranchCheck:
                    return ActivityTaskStatus.Success;
                case MaterialIconKind.SourceBranchMinus:
                    return ActivityTaskStatus.Warning;
                case MaterialIconKind.SourceBranchRemove:
                    return ActivityTaskStatus.Errored;
            }

        return null;
    }
}
