using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Serilog;
using System;
using System.Globalization;
using System.IO;

namespace KitX.Dashboard.Converters;

public class Base64ToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var location = $"{nameof(Base64ToIconConverter)}.{nameof(Convert)}";

        try
        {
            var base64 = value as string;

            ArgumentNullException.ThrowIfNull(base64, nameof(value));

            var src = System.Convert.FromBase64String(base64);

            using var ms = new MemoryStream(src);

            return new Bitmap(ms);
        }
        catch (Exception e)
        {
            Log.Warning(
                e,
                $"In {location}: Failed to transform icon from base64 to byte[] or create bitmap from `MemoryStream`. {e.Message}"
            );

            return App.DefaultIcon;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
