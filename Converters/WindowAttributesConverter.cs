using Avalonia.Controls;
using Common.BasicHelper.UI.Screen;

#pragma warning disable CS8629 // 可为 null 的值类型可为 null。

namespace KitX_Dashboard.Converters;

internal class WindowAttributesConverter
{
    /// <summary>
    /// 坐标回正
    /// </summary>
    /// <param name="input">传入的坐标</param>
    /// <param name="isLeft">是否是距左距离</param>
    /// <returns>回正的坐标</returns>
    internal static int PositionCameCenter(int input, bool isLeft, Screens screens, Resolution win)
        => isLeft
        ? (input == -1 ? (screens.Primary.WorkingArea.Width - (int)(double)win.Width) / 2 : input)
        : (input == -1 ? (screens.Primary.WorkingArea.Height - (int)(double)win.Height) / 2 : input);
}

#pragma warning restore CS8629 // 可为 null 的值类型可为 null。
