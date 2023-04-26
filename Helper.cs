using Common.Activity;
using Common.BasicHelper.IO;
using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Names;
using LiteDB;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using Activity = Common.Activity.Activity;

namespace KitX_Dashboard;

public static class Helper
{
    private static readonly object _activityRecordLock = new();

    /// <summary>
    /// 启动时检查
    /// </summary>
    public static void StartUpCheck()
    {
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
                flushToDiskInterval: new(0, 0, ConfigManager.AppConfig.Log.LogFileFlushInterval),
                restrictedToMinimumLevel: ConfigManager.AppConfig.Log.LogLevel,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: ConfigManager.AppConfig.Log.LogFileMaxCount
            )
            .CreateLogger();

        Log.Information("KitX Dashboard Started.");

        #endregion

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Error(ex, $"Au oh! Fatal: {ex.Message}");
        };

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

        Program.TasksManager = new();

        #endregion

        #region 初始化 WebManager

        Program.TasksManager.SignalRun(nameof(SignalsNames.MainWindowInitSignal), () =>
        {
            new Thread(async () =>
            {
                Thread.Sleep(ConfigManager.AppConfig.Web.DelayStartSeconds * 1000);
                Program.WebManager = await new WebManager().Start();
            }).Start();
        });

        #endregion

        #region 初始化数据记录管理器

        StatisticsManager.Start();

        #endregion

    }

    /// <summary>
    /// 初始化环境
    /// </summary>
    public static void InitEnvironment()
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
    public static void InitDataBase()
    {
        try
        {
            var dir = Path.GetFullPath(GlobalInfo.DataPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var dbfile = Path.GetFullPath(GlobalInfo.ActivitiesDataBaseFilePath);
            using var db = new LiteDatabase(dbfile);
            Program.ActivitiesDataBase = db;
            string colName = DateTime.UtcNow.ToString("yyyy_MM").Num2UpperChar();
            var col = db.GetCollection<Activity>(colName);
            var activity = new Activity()
            {
                Creator = new() { GlobalInfo.AppFullName },
                Assign = null,
                Closer = new() { GlobalInfo.AppFullName },
                StartTime = new() { DateTime.UtcNow },
                EndTime = new() { DateTime.UtcNow },
                IconKind = Material.Icons.MaterialIconKind.Play,
                Labels = null,
                Name = nameof(ActivityNames.AppStart),
                Title = ActivityTitles.AppStart,
                Tasks = null,
                Sort = nameof(ActivitySortNames.DashboardEvent),
                Result = Result.Success,
                Progress = new()
                {
                    Type = Progress.ProgressType.Tasks,
                    TasksValue = (1, 1)
                }
            };
            RecordActivity(activity, colName, new Action(() =>
            {
                activity.ID = ConfigManager.AppConfig.Activity.TotalRecorded + 1;
            }), x => x.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "In Helper.InitDataBase()");
        }
    }

    /// <summary>
    /// 检查当前是否是单进程状态
    /// </summary>
    public static void SingleProcessCheck()
    {
        Process[] processnow = Process.GetProcesses();
        int count = 0;
        foreach (var item in processnow)
        {
            if (item.ProcessName.Replace(".exe", "").Equals("KitX Dashboard"))
                ++count;
            if (count >= 2) Environment.Exit(0);
        }
    }

    /// <summary>
    /// 读取资源
    /// </summary>
    public static async void LoadResource()
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
    /// 记录活动
    /// </summary>
    /// <param name="activity">活动</param>
    /// <param name="colName">集合名称</param>
    /// <param name="action">添加后行动</param>
    /// <param name="keySelector">键选择器</param>
    public static void RecordActivity
    (
        Activity activity,
        string colName,
        Action action,
        Expression<Func<Activity, int>> keySelector
    )
    {
        lock (_activityRecordLock)
        {
            var col = Program.ActivitiesDataBase?.GetCollection<Activity>(colName);

            col?.Insert(activity);

            action();

            col?.Update(activity);

            col?.EnsureIndex(keySelector);

            ConfigManager.AppConfig.Activity.TotalRecorded += 1;
        }
    }

    /// <summary>
    /// 导入插件
    /// </summary>
    /// <param name="kxpPath">.kxp Path</param>
    public static void ImportPlugin(string kxpPath)
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

        ConfigManager.SaveConfigs();

        Log.CloseAndFlush();

        Program.WebManager?.Stop();
        Program.WebManager?.Dispose();

        Program.ActivitiesDataBase?.Commit();
        Program.ActivitiesDataBase?.Dispose();

        GlobalInfo.Running = false;

        new Thread(() =>
        {
            try
            {
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

//                      __..-----')
//            ,.--._ .-'_..--...-'
//           '-"'. _/_ /  ..--''""'-.
//           _.--""...:._:(_ ..:"::. \
//        .-' ..::--""_(##)#)"':. \ \)    \ _|_ /
//       /_:-:'/  :__(##)##)    ): )   '-./'   '\.-'
//       "  / |  :' :/""\///)  /:.'    --(       )--
//         / :( :( :(   (#//)  "       .-'\.___./'-.
//        / :/|\ :\_:\   \#//\            /  |  \
//        |:/ | ""--':\   (#//)              '
//        \/  \ :|  \ :\  (#//)
//             \:\   '.':. \#//\
//              ':|    "--'(#///)
//                         (#///)
//                         (#///)         ___/""\     
//                          \#///\           oo##
//                          (##///)         `-6 #
//                          (##///)          ,'`.
//                          (##///)         // `.\
//                          (##///)        ||o   \\
//                           \##///\        \-+--//
//                           (###///)       :_|_(/
//                           (sjw////)__...--:: :...__
//                           (#/::'''        :: :     ""--.._
//                      __..-'''           __;: :            "-._
//              __..--""                  `---/ ;                '._
//     ___..--""                             `-'                    "-..___
//       (_ ""---....___                                     __...--"" _)
//         """--...  ___"""""-----......._______......----"""     --"""
//                       """"       ---.....   ___....----

