using System.Runtime.InteropServices;

namespace KitX.Dashboard.Specific.MsWindows.Graphics;

public partial class DpiUtils
{
    [LibraryImport("user32.dll")]
    private static partial int GetDpiForSystem();

    private const int DefaultDpi = 96;

    public static int GetDpi() => GetDpiForSystem();

    public static float GetScale()
    {
        int dpi = GetDpiForSystem();
        float scale = (float)dpi / DefaultDpi;

        return scale;
    }
}
