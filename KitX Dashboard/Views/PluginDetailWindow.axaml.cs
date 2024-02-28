using Avalonia.Controls;
using Avalonia.Media;
using Common.BasicHelper.Graphics.Screen;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.Shared.CSharp.Plugin;
using Serilog;
using System;

namespace KitX.Dashboard.Views;

public partial class PluginDetailWindow : Window
{
    private readonly PluginDetailWindowViewModel viewModel = new();

    public PluginDetailWindow()
    {
        var location = $"{nameof(PluginDetailWindow)}.ctor";

        InitializeComponent();

        var screen = Screens.ScreenFromWindow(this);

        if (screen is not null)
        {
            var suggest = Resolution.Suggest(
                Resolution.Parse("2560x1440"),
                Resolution.Parse("820x500"),
                Resolution.Parse($"{screen.Bounds.Width}x{screen.Bounds.Height}")
            ).Integerization();

            if (suggest is not null)
                ClientSize = new(suggest.Width ?? 820, suggest.Height ?? 500);
        }

        if (OperatingSystem.IsWindows() == false)
        {
            try
            {
                Background = Resources["ThemePrimaryAccent"] as SolidColorBrush;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"In {location}: {ex.Message}");
            }
        }

        KeyDown += (x, y) =>
        {
            switch (y.Key)
            {
                case Avalonia.Input.Key.F5:
                    Width = 820;
                    Height = 500;
                    break;
            }
        };

        Opened += (_, _) => viewModel.InitFunctionsAndTags();

        EventService.OnExiting += Close;
    }

    public PluginDetailWindow SetPluginInfo(PluginInfo ps)
    {
        viewModel.PluginDetail = ps;

        DataContext = viewModel;

        return this;
    }
}
