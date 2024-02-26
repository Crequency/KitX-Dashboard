using Avalonia.Data.Converters;
using KitX.Shared.Device;
using System;
using System.Globalization;

namespace KitX.Dashboard.Converters;

public class PluginsServerAddressConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DeviceInfo info) return null;

        return $"{info.Device.IPv4}:{info.PluginsServerPort}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class DevicesServerAddressConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DeviceInfo info) return null;

        return $"{info.Device.IPv4}:{info.DevicesServerPort}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class DeviceLastOnLineTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime time) return null;

        return time.ToLocalTime().ToString("yyyy.MM.dd HH:mm:ss");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
