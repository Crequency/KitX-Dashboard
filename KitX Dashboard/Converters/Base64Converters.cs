using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Serilog;

namespace KitX.Dashboard.Converters;

public class Base64ToIconConverter : IValueConverter
{
    /// <summary>
    /// Decoded icon cache keyed by base64 (D4) — plugin grids re-evaluate this
    /// converter for every list item on each refresh, so without a cache every
    /// refresh re-decoded every icon (and leaked the old bitmaps).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Bitmap> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        const string location = $"{nameof(Base64ToIconConverter)}.{nameof(Convert)}";

        try
        {
            var base64 = value as string;

            ArgumentNullException.ThrowIfNull(base64, nameof(value));

            if (_cache.TryGetValue(base64, out var cached))
                return cached;

            var src = System.Convert.FromBase64String(base64);

            using var ms = new MemoryStream(src);

            var bitmap = new Bitmap(ms);

            _cache[base64] = bitmap;

            return bitmap;
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
