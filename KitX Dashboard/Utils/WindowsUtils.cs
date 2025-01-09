using System;
using Avalonia.Platform;
using Common.BasicHelper.Graphics.Screen;
using KitX.Dashboard.Specific.MsWindows.Graphics;

namespace KitX.Dashboard.Utils;

internal static class WindowsUtils
{
    internal static Resolution SuggestResolution(
        this Resolution res,
        Screen? screen,
        out Resolution? notScaled
    )
    {
        notScaled = null;

        var condition = res.Width - 1280.0 < 0.1 && res.Height - 720.0 < 0.1 && screen is not null;

        if (!condition)
            return res.Integerization();

        var suggest = Resolution
            .Suggest(
                Resolution.Parse("2560x1440"),
                Resolution.Parse("1280x720"),
                Resolution.Parse($"{screen!.Bounds.Width}x{screen.Bounds.Height}")
            )
            .Integerization();

        if (OperatingSystem.IsWindows())
        {
            notScaled = suggest.Clone();

            float scale = DpiUtils.GetScale();

            suggest.Width /= scale;
            suggest.Height /= scale;

            suggest = suggest.Integerization();
        }

        if (OperatingSystem.IsMacOS()) { }

        if (OperatingSystem.IsLinux()) { }

        return suggest;
    }
}
