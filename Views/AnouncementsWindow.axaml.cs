using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Common.BasicHelper.Graphics.Screen;
using KitX_Dashboard.Converters;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using KitX_Dashboard.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;

namespace KitX_Dashboard.Views;

public partial class AnouncementsWindow : Window
{
    private readonly AnouncementsWindowViewModel viewModel = new();

    public AnouncementsWindow()
    {
        InitializeComponent();

        viewModel.Window = this;

        SuggestResolutionAndLocation();

        DataContext = viewModel;

        var nowRes = Resolution.Parse($"" +
            $"{ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width}" +
            $"x{ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height}");

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

        EventService.OnExiting += Close;

#if DEBUG
        this.AttachDevTools();
#endif

    }

    private void SuggestResolutionAndLocation()
    {
        if (ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width == 1280
            && ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height == 720)
        {
            var suggest = Resolution.Suggest(
                Resolution.Parse("2560x1440"),
                Resolution.Parse("1280x720"),
                Resolution.Parse($"{Screens.Primary.Bounds.Width}x" +
                $"{Screens.Primary.Bounds.Height}")).Integerization();

            if (suggest.Width != null
                && suggest.Height != null)
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SaveMetaData()
    {
        ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Left = Position.X;
        ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Top = Position.Y;
        ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width = Width;
        ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height = Height;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosed(e);

        SaveMetaData();
    }
}

//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;:cloc;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;::;;;;;;;;;;;;;lk00dc;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;:oxc;;;;;;;;;;;;clddc:;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;lO0d:;;;;;;;;;;;;;;:;;;:c:::;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;::::::lkKXOo::::::;;;;;;;;;;;:lkOkl;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;:cdkO00KXXXXK00OOxl:;;;;;;;;;;:lxOxl:;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;:ldOKXXXXXXX0xl:;;;;;;;;;;;;;;:c;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;lOXXXXXX0o;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;o0XKOk0XKx:;;;;;;;;;;;;;;;;:c:;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;ckOxl::cdkOo;;;;;;;;;;;;;;:lxOxl:;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;cl:;;;;;;:cl:;;;;;;;;;;;;;;lkOkl;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;:;;;:::::;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;codxl;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;ck0Odc;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;:clo:;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
//  ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
