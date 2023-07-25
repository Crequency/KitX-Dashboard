using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Common.BasicHelper.Graphics.Screen;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using KitX_Dashboard.Converters;
using KitX_Dashboard.Data;
using KitX_Dashboard.Generators;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Names;
using KitX_Dashboard.Services;
using KitX_Dashboard.ViewModels;
using Serilog;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Timers;

namespace KitX_Dashboard.Views;

public partial class MainWindow : CoreWindow
{
    private readonly MainWindowViewModel viewModel = new();

    /// <summary>
    /// 主窗体的构造函数
    /// </summary>
    public MainWindow()
    {
        var location = $"{nameof(MainWindow)}";

        InitializeComponent();

        Program.MainWindow = this;

        Resources["MainWindow"] = this;

        DataContext = viewModel;

        SuggestResolutionAndLocation();

        var nowRes = Resolution.Parse(
            $"{ConfigManager.AppConfig.Windows.MainWindow.Window_Width}" +
            $"x{ConfigManager.AppConfig.Windows.MainWindow.Window_Height}"
        );

        // 设置窗体坐标
        try
        {
            Position = new(
                WindowAttributesConverter.PositionCameCenter(
                    ConfigManager.AppConfig.Windows.MainWindow.Window_Left,
                    true, Screens, nowRes
                ),
                WindowAttributesConverter.PositionCameCenter(
                    ConfigManager.AppConfig.Windows.MainWindow.Window_Top,
                    false, Screens, nowRes
                )
            );
        }
        catch (Exception e)
        {
            Log.Error(e, $"In {location}: {e.Message}");
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Width = ConfigManager.AppConfig.Windows.MainWindow.Window_Width;
                Height = ConfigManager.AppConfig.Windows.MainWindow.Window_Height + 30;
            }
            else
            {
                Width = ConfigManager.AppConfig.Windows.MainWindow.Window_Width;
                Height = ConfigManager.AppConfig.Windows.MainWindow.Window_Height;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, $"In {location}: {e.Message}");

            ClientSize = new(800, 600);
        }

        try
        {
            Program.SignalTasksManager?.SignalRun(
                nameof(SignalsNames.MainWindowOpenedSignal),
                () => WindowState = ConfigManager.AppConfig.Windows.MainWindow.WindowState
            );

            if (ConfigManager.AppConfig.Windows.MainWindow.IsHidden)
                Program.SignalTasksManager?.SignalRun(
                    nameof(SignalsNames.MainWindowOpenedSignal),
                    Hide
                );
        }
        catch (Exception e)
        {
            Log.Error(e, $"In {location}: {e.Message}");
        }

        InitMainWindow();

#if DEBUG
        this.AttachDevTools();
#endif
    }

    /// <summary>
    /// 建议分辨率和位置
    /// </summary>
    private void SuggestResolutionAndLocation()
    {
        if (ConfigManager.AppConfig.Windows.MainWindow.Window_Width == 1280 &&
            ConfigManager.AppConfig.Windows.MainWindow.Window_Height == 720)
        {
            var suggest = Resolution.Suggest(
                Resolution.Parse("2560x1440"),
                Resolution.Parse("1280x720"),
                Resolution.Parse(
                    $"{Screens.Primary.Bounds.Width}x" +
                    $"{Screens.Primary.Bounds.Height}"
                )
            ).Integerization();

            if (suggest.Width is not null && suggest.Height is not null)
            {
                ConfigManager.AppConfig.Windows.MainWindow.Window_Width = (double)suggest.Width;
                ConfigManager.AppConfig.Windows.MainWindow.Window_Height = (double)suggest.Height;
            }
        }
    }

    /// <summary>
    /// 初始化主窗体
    /// </summary>
    private void InitMainWindow()
    {
        //  导航到上次关闭时界面
        MainNavigationView.SelectedItem = this.FindControl<NavigationViewItem>(SelectedPageName);

        var fluentAvaloniaTheme = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();

        if (fluentAvaloniaTheme is null) return;

        //  如果主题不设置为 `跟随系统` 则手动变更主题
        if (!ConfigManager.AppConfig.App.Theme.Equals("Follow"))
            fluentAvaloniaTheme.RequestedTheme = ConfigManager.AppConfig.App.Theme;

        //  透明度变更事件, 让透明度变更立即生效
        EventService.MicaOpacityChanged += () =>
        {
            if (!ConfigManager.AppConfig.Windows.MainWindow.EnabledMica) return;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            if (!IsWindows11) return;

            TryEnableMicaEffect(fluentAvaloniaTheme);
        };

        //  每 Interval 更新一次招呼语
        UpdateGreetingText();

        EventService.LanguageChanged += () => UpdateGreetingText();

        EventService.GreetingTextIntervalUpdated += () => UpdateGreetingText();

        var timer = new Timer()
        {
            AutoReset = true,
            Interval = 1000 * 60 * ConfigManager.AppConfig.Windows.MainWindow.GreetingUpdateInterval
        };

        timer.Elapsed += (_, _) => UpdateGreetingText();

        timer.Start();

        Program.SignalTasksManager?.RaiseSignal(nameof(SignalsNames.MainWindowInitSignal));
    }

    /// <summary>
    /// 保存对配置文件的修改
    /// </summary>
    private static void SaveChanges()
    {
        EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
    }

    /// <summary>
    /// 更新招呼语
    /// </summary>
    internal void UpdateGreetingText()
    {
        try
        {
            if (Application.Current is null) return;

            Application.Current.Resources.MergedDictionaries[0]
                .TryGetResource(GreetingTextGenerator.GetKey(), out object? text);

            if (text is null) return;

            Dispatcher.UIThread.Post(() =>
            {
                Resources["GreetingText"] = text as string;
            });
        }
        catch (ArgumentOutOfRangeException e)
        {
            Log.Warning(e, $"No Language Resources Loaded.");
        }
    }

    /// <summary>
    /// 通过名称获取页面类型
    /// </summary>
    /// <param name="name">页面名称</param>
    /// <returns>页面类型</returns>
    private static Type GetPageTypeFromName(string name) => name switch
    {
        "Page_Home" => typeof(Pages.HomePage),
        "Page_Lib" => typeof(Pages.LibPage),
        "Page_Repo" => typeof(Pages.RepoPage),
        "Page_Account" => typeof(Pages.AccountPage),
        "Page_Settings" => typeof(Pages.SettingsPage),
        "Page_Market" => typeof(Pages.MarketPage),
        "Page_Device" => typeof(Pages.DevicePage),
        "Page_Factory" => typeof(Pages.FactoryPage),
        _ => typeof(Pages.HomePage),
    };

    /// <summary>
    /// 已选择的页面名称
    /// </summary>
    private static string SelectedPageName
    {
        get => ConfigManager.AppConfig.Windows.MainWindow.Tags["SelectedPage"];
        set
        {
            ConfigManager.AppConfig.Windows.MainWindow.Tags["SelectedPage"] = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 前台页面切换事件
    /// </summary>
    /// <param name="sender">被点击的 NavigationViewItem</param>
    /// <param name="e">路由事件参数</param>
    private void MainNavigationView_SelectionChanged(
        object? sender,
        NavigationViewSelectionChangedEventArgs e)
    {
        try
        {
            if (sender is null) return;

            var navView = sender as NavigationView;

            if (navView?.SelectedItem is not Control control || control.Tag is null) return;

            var pageName = control.Tag.ToString();

            if (pageName is null) return;

            SelectedPageName = pageName;

            MainFrame.Navigate(GetPageTypeFromName(SelectedPageName));
        }
        catch (NullReferenceException o)
        {
            Log.Warning(o, o.Message);
        }
    }

    /// <summary>
    /// 储存元数据
    /// </summary>
    private void SaveMetaData()
    {
        if (WindowState != WindowState.Maximized)
        {
            if (WindowState != WindowState.Minimized)
            {
                ConfigManager.AppConfig.Windows.MainWindow.Window_Left = Position.X;
                ConfigManager.AppConfig.Windows.MainWindow.Window_Top = Position.Y;
            }
            if (OperatingSystem.IsWindows())
            {
                ConfigManager.AppConfig.Windows.MainWindow.Window_Width = Width;
                ConfigManager.AppConfig.Windows.MainWindow.Window_Height = Height - 30;
            }
            else
            {
                ConfigManager.AppConfig.Windows.MainWindow.Window_Width = Width;
                ConfigManager.AppConfig.Windows.MainWindow.Window_Height = Height;
            }
        }

        ConfigManager.AppConfig.Windows.MainWindow.Tags["SelectedPage"] = SelectedPageName;
    }

    /// <summary>
    /// 窗口状态改变事件
    /// </summary>
    /// <param name="state">窗口状态</param>
    protected override void HandleWindowStateChanged(WindowState state)
    {
        ConfigManager.AppConfig.Windows.MainWindow.WindowState = state;
        ConfigManager.AppConfig.Windows.MainWindow.IsHidden = false;

        SaveChanges();

        base.HandleWindowStateChanged(state);
    }

    /// <summary>
    /// 正在关闭窗口时事件
    /// </summary>
    /// <param name="e">关闭事件参数</param>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        SaveMetaData();

        if (!GlobalInfo.Exiting)
        {
            e.Cancel = true;

            Hide();

            ConfigManager.AppConfig.Windows.MainWindow.IsHidden = true;

            SaveChanges();
        }
        else
        {
            (Resources["TrayIcon"] as TrayIcon)?.Dispose();
        }
    }

    /// <summary>
    /// 窗体正在启动事件
    /// </summary>
    /// <param name="e">窗体启动参数</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);



        var thm = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();

        if (thm is null) return;

        thm.RequestedThemeChanged += OnRequestedThemeChanged;

        if (!ConfigManager.AppConfig.Windows.MainWindow.EnabledMica) return;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        if (!IsWindows11) return;

        if (thm.RequestedTheme == FluentAvaloniaTheme.HighContrastModeString) return;



        TransparencyBackgroundFallback = Brushes.Transparent;

        TransparencyLevelHint = WindowTransparencyLevel.Mica;

        TryEnableMicaEffect(thm);

        thm.ForceWin32WindowToTheme(this);

        Program.SignalTasksManager?.RaiseSignal(nameof(SignalsNames.MainWindowOpenedSignal));
    }

    /// <summary>
    /// 主题正在更改请求事件
    /// </summary>
    /// <param name="sender">FluentAvaloniaTheme</param>
    /// <param name="args">主题正在更改请求参数</param>
    private void OnRequestedThemeChanged(
        FluentAvaloniaTheme sender,
        RequestedThemeChangedEventArgs args)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (IsWindows11 && args.NewTheme != FluentAvaloniaTheme.HighContrastModeString)
                TryEnableMicaEffect(sender);
            else if (args.NewTheme == FluentAvaloniaTheme.HighContrastModeString)
                SetValue(BackgroundProperty, AvaloniaProperty.UnsetValue);
        }
    }

    /// <summary>
    /// 尝试启用云母特效
    /// </summary>
    /// <param name="thm">FluentAvaloniaTheme</param>
    private void TryEnableMicaEffect(FluentAvaloniaTheme thm)
    {
        _ = this.TryFindResource(
            "SolidBackgroundFillColorBase",
            out var value
        );

        if (thm.RequestedTheme == FluentAvaloniaTheme.DarkModeString)
        {
            var color = value is null
                ? new Color2(32, 32, 32)
                : (Color2)(Color)value;

            color = color.LightenPercent(-0.8f);

            Background = new ImmutableSolidColorBrush(
                color,
                ConfigManager.AppConfig.Windows.MainWindow.MicaOpacity
            );
        }
        else if (thm.RequestedTheme == FluentAvaloniaTheme.LightModeString)
        {
            var color = value is null
                ? new Color2(243, 243, 243)
                : (Color2)(Color)value;

            color = color.LightenPercent(0.5f);

            Background = new ImmutableSolidColorBrush(
                color,
                ConfigManager.AppConfig.Windows.MainWindow.MicaOpacity
            );
        }
    }
}
