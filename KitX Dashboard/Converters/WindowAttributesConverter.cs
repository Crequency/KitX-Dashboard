using Avalonia.Controls;
using Avalonia.Platform;
using Common.BasicHelper.Graphics.Screen;

namespace KitX.Dashboard.Converters;

internal class WindowAttributesConverter
{
    internal static Distances PositionCameCenter(Distances location, Screen? screen, Resolution win)
    {
        if (location.Left == -1)
            location.Left = ((screen?.WorkingArea.Width ?? 2560) - (int)win.Width!) / 2;

        if (location.Top == -1)
            location.Top = (screen?.WorkingArea.Height ?? 1440 - (int)win.Height!) / 2;

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
