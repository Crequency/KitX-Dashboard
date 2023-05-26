using Avalonia.Controls;
using Avalonia.Media;
using Common.BasicHelper.Graphics.Screen;
using KitX.Web.Rules;
using KitX_Dashboard.Services;
using KitX_Dashboard.ViewModels;
using System;

namespace KitX_Dashboard.Views;

public partial class PluginDetailWindow : Window
{
    private readonly PluginDetailWindowViewModel viewModel = new();

    public PluginDetailWindow()
    {
        InitializeComponent();

        Resources["ThisWindow"] = this;

        var suggest = Resolution.Suggest(
            Resolution.Parse("2560x1440"),
            Resolution.Parse("820x500"),
            Resolution.Parse(
                $"{Screens.Primary.Bounds.Width}x{Screens.Primary.Bounds.Height}"
            )
        ).Integerization();

        Width = suggest.Width ?? 820;
        Height = suggest.Height ?? 500;

        if (OperatingSystem.IsWindows() == false)
        {
            Background = Resources["ThemePrimaryAccent"] as SolidColorBrush;
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

    public PluginDetailWindow SetPluginStruct(PluginStruct ps)
    {
        viewModel.PluginDetail = ps;

        DataContext = viewModel;

        return this;
    }
}
