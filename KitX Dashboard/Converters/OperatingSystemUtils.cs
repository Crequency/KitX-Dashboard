using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KitX.Shared.CSharp.Device;
using Material.Icons;

namespace KitX.Dashboard.Converters;

public static class OperatingSystemUtils
{
    public static OperatingSystems GetOSType()
    {
        if (OperatingSystem.IsAndroid())
            return OperatingSystems.Android;
        if (OperatingSystem.IsBrowser())
            return OperatingSystems.Browser;
        if (OperatingSystem.IsFreeBSD())
            return OperatingSystems.FreeBSD;
        if (OperatingSystem.IsIOS())
            return OperatingSystems.IOS;
        if (OperatingSystem.IsLinux())
            return OperatingSystems.Linux;
        if (OperatingSystem.IsMacCatalyst())
            return OperatingSystems.MacCatalyst;
        if (OperatingSystem.IsMacOS())
            return OperatingSystems.MacOS;
        if (OperatingSystem.IsTvOS())
            return OperatingSystems.TvOS;
        if (OperatingSystem.IsWatchOS())
            return OperatingSystems.WatchOS;
        if (OperatingSystem.IsWindows())
            return OperatingSystems.Windows;

        return OperatingSystems.Unknown;
    }
}

public class OperatingSystemToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;

        var os = (OperatingSystems)value;

        return os switch
        {
            OperatingSystems.Unknown => MaterialIconKind.QuestionMarkCircle,
            OperatingSystems.Android => MaterialIconKind.Android,
            OperatingSystems.Browser => MaterialIconKind.MicrosoftEdge,
            OperatingSystems.FreeBSD => MaterialIconKind.Freebsd,
            OperatingSystems.IOS => MaterialIconKind.AppleIos,
            OperatingSystems.Linux => MaterialIconKind.Linux,
            OperatingSystems.MacCatalyst => MaterialIconKind.Apple,
            OperatingSystems.MacOS => MaterialIconKind.AppleKeyboardCommand,
            OperatingSystems.TvOS => MaterialIconKind.Apple,
            OperatingSystems.WatchOS => MaterialIconKind.Apple,
            OperatingSystems.Windows => MaterialIconKind.MicrosoftWindows,
            OperatingSystems.IoT => MaterialIconKind.Chip,
            OperatingSystems.AppleVisionOS => MaterialIconKind.Apple,
            _ => MaterialIconKind.QuestionMarkCircle,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
