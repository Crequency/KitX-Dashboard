using Avalonia.Platform;
using Common.BasicHelper.Graphics.Screen;

namespace KitX.Dashboard.Utils;

internal static class WindowsUtils
{
    internal static Resolution SuggestResolution(this Resolution res, Screen? screen)
    {
        if (res.Width == 1280 && res.Height == 720 && screen is not null)
        {
            var suggest = Resolution.Suggest(
                Resolution.Parse("2560x1440"),
                Resolution.Parse("1280x720"),
                Resolution.Parse($"{screen.WorkingArea.Width}x{screen.WorkingArea.Height}")
            ).Integerization();

            return suggest;
        }

        return res;
    }
}
