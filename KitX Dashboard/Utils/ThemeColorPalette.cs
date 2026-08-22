using Avalonia;
using Avalonia.Media;

namespace KitX.Dashboard.Utils;

/// <summary>
/// Builds the theme accent color palette (primary accent + transparency variants)
/// into the application's resource dictionary (D6 convergence — previously duplicated
/// in App.CalculateThemeColor and Settings_PersonaliseViewModel.ColorConfirmedCommand).
/// </summary>
internal static class ThemeColorPalette
{
    internal static void ApplyTo(Application app, Color c)
    {
        app.Resources["ThemePrimaryAccent"] = new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));

        for (char i = 'A'; i <= 'E'; ++i)
        {
            app.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] = new SolidColorBrush(
                new Color((byte)(170 + (i - 'A') * 17), c.R, c.G, c.B)
            );
        }
        for (int i = 1; i <= 9; ++i)
        {
            app.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] = new SolidColorBrush(
                new Color((byte)(i * 10 + i), c.R, c.G, c.B)
            );
        }
    }
}
