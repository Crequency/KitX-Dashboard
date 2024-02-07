using Avalonia;
using Avalonia.Controls;
using Common.BasicHelper.Graphics.Screen;
using KitX.Dashboard.Converters;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using System.Collections.Generic;

namespace KitX.Dashboard.Views;

public partial class AnouncementsWindow : Window
{
    private readonly AnouncementsWindowViewModel viewModel = new();

    private bool closed = false;

    public AnouncementsWindow()
    {
        InitializeComponent();

        viewModel.Window = this;

        SuggestResolutionAndLocation();

        DataContext = viewModel;

        var nowRes = Resolution.Parse(
            $"{ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width}" +
            $"x{ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height}"
        );

        // 设置窗体坐标

        Position = new(
            WindowAttributesConverter.PositionCameCenter(
                ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Left,
                true, Screens, nowRes
            ),
            WindowAttributesConverter.PositionCameCenter(
                ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Top,
                false, Screens, nowRes
            )
        );

        EventService.OnExiting += () =>
        {
            if (!closed)
                Close();
        };

#if DEBUG
        this.AttachDevTools();
#endif

    }

    private void SuggestResolutionAndLocation()
    {
        if (Screens.Primary is null) return;

        if (ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width == 1280
            && ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height == 720)
        {
            var suggest = Resolution.Suggest(
                Resolution.Parse("2560x1440"),
                Resolution.Parse("1280x720"),
                Resolution.Parse(
                    $"{Screens.Primary.Bounds.Width}x{Screens.Primary.Bounds.Height}"
                )
            ).Integerization();

            if (suggest.Width is not null && suggest.Height is not null)
            {
                ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width = (double)suggest.Width;
                ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height = (double)suggest.Height;
            }
        }
    }

    internal void UpdateSource(Dictionary<string, string> src, List<string> readed)
    {
        viewModel.Sources = src;
        viewModel.Readed = readed;
    }

    private void SaveMetaData()
    {
        if (WindowState != WindowState.Minimized)
        {
            ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Left = Position.X;
            ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Top = Position.Y;
        }

        ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width = Width;
        ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height = Height;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosed(e);

        SaveMetaData();

        closed = true;
    }
}
