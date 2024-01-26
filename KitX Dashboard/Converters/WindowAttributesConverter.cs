using Avalonia.Controls;
using Common.BasicHelper.Graphics.Screen;

namespace KitX.Dashboard.Converters;

internal class WindowAttributesConverter
{
    internal static int PositionCameCenter(int input, bool isLeft, Screens screens, Resolution win)
    {
        if (win.Width is null || win.Height is null) return 0;

        return isLeft
            ? (input == -1 ? (screens.Primary?.WorkingArea.Width ?? 2560 - (int)win.Width) / 2 : input)
            : (input == -1 ? (screens.Primary?.WorkingArea.Height ?? 1440 - (int)win.Height) / 2 : input)
            ;
    }
}
