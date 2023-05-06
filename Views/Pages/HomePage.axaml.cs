using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using KitX_Dashboard.ViewModels.Pages;
using KitX_Dashboard.Views.Controls;
using Serilog;
using System;

namespace KitX_Dashboard.Views.Pages;

public partial class HomePage : UserControl
{
    private readonly HomePageViewModel viewModel = new();

    public HomePage()
    {
        InitializeComponent();

        DataContext = viewModel;

        InitHomePage();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 初始化主页
    /// </summary>
    private void InitHomePage()
    {
        this.FindControl<NavigationView>("HomeNavigationView").SelectedItem
            = this.FindControl<NavigationViewItem>(SelectedViewName);
    }

    /// <summary>
    /// 保存对配置文件的修改
    /// </summary>
    private static void SaveChanges()
    {
        EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
    }

    private static string SelectedViewName
    {
        get => ConfigManager.AppConfig.Pages.Home.SelectedViewName;
        set
        {
            ConfigManager.AppConfig.Pages.Home.SelectedViewName = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 前台页面切换事件
    /// </summary>
    /// <param name="sender">被点击的 NavigationViewItem</param>
    /// <param name="e">路由事件参数</param>
    private void HomeNavigationView_SelectionChanged(
        object? sender,
        NavigationViewSelectionChangedEventArgs e)
    {
        var location = $"{nameof(HomePage)}.{nameof(HomeNavigationView_SelectionChanged)}";

        try
        {
            var tag = ((sender as NavigationView)?.SelectedItem as Control)?.Tag?.ToString();

            if (tag is null) return;

            SelectedViewName = tag;

            this.FindControl<Frame>("HomeFrame").Navigate(SelectedViewType());
        }
        catch (NullReferenceException o)
        {
            Log.Warning(o, o.Message);
        }
    }

    private static Type SelectedViewType() => SelectedViewName switch
    {
        "View_Recent" => typeof(Home_RecentUse),
        "View_Count" => typeof(Home_Count),
        "View_ActivityLog" => typeof(Home_ActivityLog),
        _ => typeof(Home_RecentUse),
    };
}

//
//                            __ _..._ _ 
//                            \ `)    `(/
//                            /`       \
//                            |   d  b  |
//              .-"````"=-..--\=    Y  /=
//            /`               `-.__=.'
//     _     / /\                 /o
//    ( \   / / |                 |
//     \ '-' /   &gt;    /`""--.    /
//      '---'   /    ||      |   \\
//              \___,,))      \_,,))
// 
//
