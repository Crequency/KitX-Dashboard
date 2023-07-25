using Avalonia;
using Avalonia.ReactiveUI;
using Common.BasicHelper.Core.TaskSystem;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views;
using KitX_Dashboard.Views.Pages.Controls;
using LiteDB;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace KitX_Dashboard;

internal class Program
{
    internal static SignalTasksManager? SignalTasksManager;

    internal static WebManager? WebManager;

    internal static FileWatcherManager? FileWatcherManager;

    internal static ObservableCollection<PluginCard> PluginCards = new();

    internal static ObservableCollection<DeviceCard> DeviceCards = new();

    internal static ObservableCollection<WorkshopCard> WorkshopCards = new();

    internal static LiteDatabase? ActivitiesDataBase;

    internal static CacheManager? CacheManager;

    internal static HotKeyManager? HotKeyManager;

    internal static MainWindow? MainWindow;

    internal static PluginsLaunchWindow? PluginsLaunchWindow;

    /// <summary>
    /// 主函数, 应用程序入口; 展开 summary 查看警告
    /// </summary>
    /// <param name="args">程序启动参数</param>
    /// Initialization code. Don't use any Avalonia, third-party APIs or any
    /// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    /// yet and stuff might break.
    /// 初始化代码. 请不要在 AppMain 被调用之前使用任何 Avalonia, 第三方的 API 或者 同步上下文相关的代码:
    /// 相关的代码还没有被初始化, 而且环境可能会被破坏
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            EventService.Init();

            Helper.ProcessStartupArguments(args);

            Helper.StartUpCheck();

            ConfigManager.AppConfig.App.RanTime++;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            Helper.Exit();
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.GetFullPath("./dump.log"), e.Message);

#if !DEBUG
            Environment.Exit(1);
#endif
        }
    }

    /// <summary>
    /// 构建 Avalonia 应用; 展开 summary 查看警告
    /// </summary>
    /// <returns>应用构造器</returns>
    /// Avalonia configuration, don't remove; also used by visual designer.
    /// Avalonia 配置项, 请不要删除; 同时也用于可视化设计器
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace()
        .UseReactiveUI()
        .With(
            new Win32PlatformOptions
            {
                UseWindowsUIComposition = true,
                EnableMultitouch = true,
            }
        )
        .With(
            new MacOSPlatformOptions
            {
                ShowInDock = true
            }
        )
        .With(
            new X11PlatformOptions
            {
                EnableMultiTouch = true,
            }
        );
}
