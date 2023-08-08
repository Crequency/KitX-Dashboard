using Avalonia.Controls;
using Common.BasicHelper.Graphics.Screen;

namespace KitX.Dashboard.Converters;

internal class WindowAttributesConverter
{
    /// <summary>
    /// 坐标回正
    /// </summary>
    /// <param name="input">传入的坐标</param>
    /// <param name="isLeft">是否是距左距离</param>
    /// <returns>回正的坐标</returns>
    internal static int PositionCameCenter(int input, bool isLeft, Screens screens, Resolution win)
    {
        if (win.Width is null || win.Height is null) return 0;

        return isLeft
            ? (input == -1 ? (screens.Primary?.WorkingArea.Width ?? 2560 - (int)win.Width) / 2 : input)
            : (input == -1 ? (screens.Primary?.WorkingArea.Height ?? 1440 - (int)win.Height) / 2 : input)
            ;
    }
}
