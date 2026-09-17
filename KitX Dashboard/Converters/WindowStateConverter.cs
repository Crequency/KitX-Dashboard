using Avalonia.Controls;
using KWindowState = KitX.Core.Contract.Configuration.WindowState;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Converts between KitX.Core.WindowState and Avalonia.Controls.WindowState
/// </summary>
public static class WindowStateConverter
{
    /// <summary>
    /// Converts KitX.Core.Contract.Configuration.WindowState to Avalonia.Controls.WindowState
    /// </summary>
    public static WindowState ToAvalonia(this KWindowState state)
    {
        return state switch
        {
            KWindowState.Normal => WindowState.Normal,
            KWindowState.Minimized => WindowState.Minimized,
            KWindowState.Maximized => WindowState.Maximized,
            KWindowState.FullScreen => WindowState.FullScreen,
            _ => WindowState.Normal
        };
    }

    /// <summary>
    /// Converts Avalonia.Controls.WindowState to KitX.Core.Contract.Configuration.WindowState
    /// </summary>
    public static KWindowState ToCore(this WindowState state)
    {
        return state switch
        {
            WindowState.Normal => KWindowState.Normal,
            WindowState.Minimized => KWindowState.Minimized,
            WindowState.Maximized => KWindowState.Maximized,
            WindowState.FullScreen => KWindowState.FullScreen,
            _ => KWindowState.Normal
        };
    }
}
