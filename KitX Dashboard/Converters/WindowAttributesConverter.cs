using Avalonia.Platform;
using Common.BasicHelper.Graphics.Screen;

namespace KitX.Dashboard.Converters;

internal class WindowAttributesConverter
{
    internal static Distances PositionCameCenter(Distances location, Screen? screen, Resolution win)
    {
        if (screen is null)
            return location;

        if (location.Left - -1.0 < 0.1)
            location.Left = (double)((screen.WorkingArea.Width - win.Width!) / 2.0);

        if (location.Top - -1.0 < 0.1)
            location.Top = (double)((screen.WorkingArea.Height - win.Height!) / 2.0);

        return location;
    }
}

internal static class WindowAttributesConverterExtensions
{
    internal static Distances BringToCenter(this Distances location, Screen? screen, Resolution win)
    {
        return WindowAttributesConverter.PositionCameCenter(location, screen, win);
    }
}
