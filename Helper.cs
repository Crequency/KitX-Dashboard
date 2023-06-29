using Avalonia.Threading;
using Common.BasicHelper.IO;
using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Names;
using LiteDB;
using ReactiveUI;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace KitX_Dashboard;

public static class Helper
{
    /// <summary>
    /// 处理启动参数
    /// </summary>
    /// <param name="args">参数列表</param>
    public static void ProcessStartupArguments(string[] args)
    {
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--import-plugin":
                        if (i != args.Length - 1)
                            try
                            {
                                ImportPlugin(args[i + 1]);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                            }
                        else throw new Exception("No arguments for plugin location.");
                        break;
                    case "--disable-single-process-check":
                        GlobalInfo.IsSingleProcessStartMode = false;
                        break;
                    case "--disable-config-hot-reload":
                        GlobalInfo.EnabledConfigFileHotReload = false;
                        break;
                    case "--disable-network-system-on-startup":
                        GlobalInfo.SkipNetworkSystemOnStartup = true;
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);

            Environment.Exit(ErrorCodes.StartUpArgumentsError);
        }
    }

    /// <summary>
    /// 启动时检查
    /// </summary>
    public static void StartUpCheck()
    {
        if (GlobalInfo.IsSingleProcessStartMode)
            SingleProcessCheck();

        if (GlobalInfo.EnabledConfigFileHotReload)
            Program.FileWatcherManager = new();

        ConfigManager.Init();   //  初始化配置管理器

        LoadResource();         //  加载资源

        #region 初始化日志系统

        var logdir = ConfigManager.AppConfig.Log.LogFilePath.GetFullPath();

        if (!Directory.Exists(logdir))
            Directory.CreateDirectory(logdir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                $"{logdir}Log_.log",
                outputTemplate: ConfigManager.AppConfig.Log.LogTemplate,
                rollingInterval: RollingInterval.Hour,
                fileSizeLimitBytes: ConfigManager.AppConfig.Log.LogFileSingleMaxSize,
                buffered: true,
                flushToDiskInterval: new(
                    0,
                    0,
                    ConfigManager.AppConfig.Log.LogFileFlushInterval
                ),
                restrictedToMinimumLevel: ConfigManager.AppConfig.Log.LogLevel,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: ConfigManager.AppConfig.Log.LogFileMaxCount
            )
            .CreateLogger();

        Log.Information("KitX Dashboard Started.");

        #endregion

        #region 初始化全局异常捕获

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Error(ex, $"Au oh! Fatal: {ex.Message}");
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Log.Error(e.Exception, $"Au oh! Fatal: {e.Exception.Message}");
        };

        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
        {
            Log.Error(ex, $"Au oh! Fatal: {ex.Message}");
        });

        #endregion

        #region 初始化环境

        InitEnvironment();

        #endregion

        #region 初始化缓存管理器

        Program.CacheManager = new();

        #endregion

        #region 初始化数据库

        InitDataBase();

        #endregion

        #region 初始化 TasksManager

        Program.SignalTasksManager = new();

        #endregion

        #region 初始化 WebManager

        Program.SignalTasksManager.SignalRun(nameof(SignalsNames.MainWindowInitSignal), () =>
        {
            new Thread(async () =>
            {
                Thread.Sleep(ConfigManager.AppConfig.Web.DelayStartSeconds * 1000);
                if (GlobalInfo.SkipNetworkSystemOnStartup)
                    Program.WebManager = new();
                else
                    Program.WebManager = await new WebManager().Start();
            }).Start();
        });

        #endregion

        #region 初始化数据记录管理器

        StatisticsManager.Start();

        #endregion

        #region 初始化热键系统

        Program.HotKeyManager = new HotKeyManager().Hook();

        #endregion

        #region 初始化持久的窗口

        Program.SignalTasksManager.SignalRun(nameof(SignalsNames.MainWindowInitSignal), () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Program.PluginsLaunchWindow = new();
            });
        });

        #endregion

    }

    /// <summary>
    /// 初始化环境
    /// </summary>
    private static void InitEnvironment()
    {
        var location = $"{nameof(Helper)}.{nameof(InitEnvironment)}";

        if (!Common.Algorithm.Interop.Environment.Check())
            new Thread(async () =>
            {
                try
                {
                    await Common.Algorithm.Interop.Environment.InstallAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }).Start();
    }

    /// <summary>
    /// 初始化数据库
    /// </summary>
    private static void InitDataBase()
    {
        var location = $"{nameof(Helper)}.{nameof(InitDataBase)}";

        try
        {
            var dir = GlobalInfo.DataPath.GetFullPath();

            if (!Directory.Exists(dir)) _ = Directory.CreateDirectory(dir);

            var dbfile = GlobalInfo.ActivitiesDataBaseFilePath.GetFullPath();

            var db = new LiteDatabase(dbfile);

            Program.ActivitiesDataBase = db;

            ActivityManager.RecordAppStart();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查当前是否是单进程状态
    /// </summary>
    private static void SingleProcessCheck()
    {
        var nowProcesses = Process.GetProcesses();
        var count = 0;

        foreach (var item in nowProcesses)
        {
            if (item.ProcessName.Replace(".exe", "").Equals("KitX Dashboard"))
                ++count;

            if (count >= 2) Environment.Exit(ErrorCodes.AlraedyStartedOneProcess);
        }
    }

    /// <summary>
    /// 读取资源
    /// </summary>
    private static async void LoadResource()
    {
        var location = $"{nameof(Helper)}.{nameof(LoadResource)}";

        try
        {
            GlobalInfo.KitXIconBase64 = await FileHelper.ReadAllAsync(
                $"{GlobalInfo.AssetsPath}{GlobalInfo.IconBase64FileName}".GetFullPath()
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    /// <summary>
    /// 导入插件
    /// </summary>
    /// <param name="kxpPath">.kxp Path</param>
    private static void ImportPlugin(string kxpPath)
    {
        try
        {
            if (!File.Exists(kxpPath))
            {
                Console.WriteLine($"No this file: {kxpPath}");

                throw new Exception("Plugins Package Doesn't Exist.");
            }
            else
            {
                PluginsManager.ImportPlugin(new string[] { kxpPath });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "In Helper.ImportPlugin()");
        }
    }

    /// <summary>
    /// 退出
    /// </summary>
    public static void Exit()
    {
        var location = $"{nameof(Helper)}.{nameof(Exit)}";

        new Thread(() =>
        {
            try
            {
                ActivityManager.RecordAppExit();

                Program.FileWatcherManager?.Clear();

                ConfigManager.SaveConfigs();

                Log.CloseAndFlush();

                Program.WebManager?.Stop();
                Program.WebManager?.Dispose();

                Program.ActivitiesDataBase?.Commit();
                Program.ActivitiesDataBase?.Dispose();

                GlobalInfo.Running = false;

                Thread.Sleep(GlobalInfo.LastBreakAfterExit);

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"In {location}: {ex.Message}");
            }
        }).Start();
    }
}
