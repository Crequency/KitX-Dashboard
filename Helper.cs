using Common.Activity;
using Common.BasicHelper.IO;
using Common.BasicHelper.Util.Extension;
using KitX_Dashboard.Data;
using KitX_Dashboard.Names;
using KitX_Dashboard.Services;
using LiteDB;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using Activity = Common.Activity.Activity;
using JsonSerializer = System.Text.Json.JsonSerializer;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8601 // 引用类型赋值可能为 null。

namespace KitX_Dashboard
{
    public static class Helper
    {
        /// <summary>
        /// 启动时检查
        /// </summary>
        public static void StartUpCheck()
        {
            #region 初始化 Config 并加载资源

            InitConfig();

            LoadResource();

            #endregion

            #region 初始化日志系统

            //InitLiteLogger(Program.LocalLogger);

            string logdir = Path.GetFullPath(Program.Config.Log.LogFilePath);

            if (!Directory.Exists(logdir))
                Directory.CreateDirectory(logdir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    $"{logdir}Log_.log",
                    outputTemplate: Program.Config.Log.LogTemplate,
                    rollingInterval: RollingInterval.Hour,
                    fileSizeLimitBytes: Program.Config.Log.LogFileSingleMaxSize,
                    buffered: true,
                    flushToDiskInterval: new(0, 0, Program.Config.Log.LogFileFlushInterval),
                    restrictedToMinimumLevel: Program.Config.Log.LogLevel,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: Program.Config.Log.LogFileMaxCount
                )
                .CreateLogger();

            Log.Information("KitX Dashboard Started.");

            #endregion

            #region 初始化全局异常捕获

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception)
                {
                    var ex = e.ExceptionObject as Exception;
                    Log.Error(ex, $"Au oh! Fatal: {ex.Message}");
                }
            };

            #endregion

            #region 初始化环境

            InitEnvironment();

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
                new Thread(() =>
                {
                    Thread.Sleep(Program.Config.Web.DelayStartSeconds * 1000);
                    Program.WebManager = new WebManager().Start();
                }).Start();
            });

            #endregion

            #region 初始化数据记录管理器

            StatisticsManager.InitEvents();

            StatisticsManager.RecoverOldStatistics();

            StatisticsManager.BeginRecord();

            #endregion

            #region 初始化事件

            EventHandlers.ConfigSettingsChanged += () => SaveConfig();

            EventHandlers.PluginsListChanged += () => SavePluginsListConfig();

            #endregion

            #region 初始化文件监控管理器

            if (GlobalInfo.EnabledConfigFileHotReload)
                InitFileWatchers();

            #endregion
        }

        /// <summary>
        /// 初始化文件监控器
        /// </summary>
        private static void InitFileWatchers()
        {
            Program.FileWatcherManager = new();
            var wm = Program.FileWatcherManager;
            wm.RegisterWatcher(nameof(FileWatcherNames), GlobalInfo.ConfigFilePath,
                new((x, y) =>
            {
                Log.Information($"OnChanged: {y.Name}");
                try
                {
                    lock (_configWriteLock)
                    {
                        Program.Config = JsonSerializer.Deserialize<AppConfig>(
                            File.ReadAllText(GlobalInfo.ConfigFilePath));
                        EventHandlers.Invoke(nameof(EventHandlers.OnConfigHotReloaded));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "In Config Hot Reload: ");
                }
            }));
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

        private static readonly object _configWriteLock = new();
        private static readonly object _activityRecordLock = new();

        /// <summary>
        /// 保存配置
        /// </summary>
        public static void SaveConfig()
        {
            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                IncludeFields = true,
            };

            new Thread(() =>
            {
                try
                {
                    lock (_configWriteLock)
                    {
                        FileHelper.WriteIn(GlobalInfo.ConfigFilePath,
                            JsonSerializer.Serialize(Program.Config, options));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "In Helper.SaveConfig()");
                }
            }).Start();
        }

        /// <summary>
        /// 保存插件列表配置
        /// </summary>
        public static void SavePluginsListConfig()
        {
            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                IncludeFields = true,
            };

            new Thread(() =>
            {
                try
                {
                    FileHelper.WriteIn(GlobalInfo.PluginsListConfigFilePath,
                        JsonSerializer.Serialize(Program.PluginsList, options));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "In Helper.SavePluginsListConfig()");
                }
            }).Start();
        }

        /// <summary>
        /// 读取配置
        /// </summary>
        public static async void LoadConfig()
        {
            try
            {
                Program.Config = JsonSerializer.Deserialize<AppConfig>(
                    await FileHelper.ReadAllAsync(GlobalInfo.ConfigFilePath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "In Helper.LoadConfig()");
                Program.Config = new AppConfig();
            }
        }

        /// <summary>
        /// 读取插件列表配置
        /// </summary>
        public static async void LoadPluginsListConfig()
        {
            try
            {
                Program.PluginsList = JsonSerializer.Deserialize<PluginsList>(
                    await FileHelper.ReadAllAsync(GlobalInfo.PluginsListConfigFilePath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "In Helper.LoadPluginsListConfig()");
            }
        }

        /// <summary>
        /// 读取资源
        /// </summary>
        public static async void LoadResource()
        {
            try
            {
                GlobalInfo.KitXIconBase64 = await FileHelper.ReadAllAsync(Path.GetFullPath(
                    $"{GlobalInfo.AssetsPath}{GlobalInfo.IconBase64FileName}"
                ));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "In Helper.LoadResource()");
            }
        }

        /// <summary>
        /// 初始化配置
        /// </summary>
        public static void InitConfig()
        {
            try
            {
                if (!Directory.Exists(Path.GetFullPath(GlobalInfo.ConfigPath)))
                    _ = Directory.CreateDirectory(Path.GetFullPath(GlobalInfo.ConfigPath));
                if (!File.Exists(Path.GetFullPath(GlobalInfo.ConfigFilePath))) SaveConfig();
                else LoadConfig();
                if (!File.Exists(Path.GetFullPath(GlobalInfo.PluginsListConfigFilePath)))
                    SavePluginsListConfig();
                else LoadPluginsListConfig();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "In Helper.InitConfig()");
            }
        }

        /// <summary>
        /// 保存信息
        /// </summary>
        public static void SaveInfo()
        {
            SaveConfig();
            SavePluginsListConfig();
            Log.CloseAndFlush();
        }

        /// <summary>
        /// 退出
        /// </summary>
        public static void Exit()
        {
            Log.CloseAndFlush();

            Program.WebManager?.Stop();
            Program.WebManager?.Dispose();

            Program.ActivitiesDataBase?.Commit();
            Program.ActivitiesDataBase?.Dispose();

            GlobalInfo.Running = false;
        }

        /// <summary>
        /// 初始化环境
        /// </summary>
        public static void InitEnvironment()
        {
            #region 检查 Common.Algorithm 库环境并安装环境
            if (!Common.Algorithm.Interop.Environment.CheckEnvironment())
                new Thread(() =>
                {
                    try
                    {
                        Common.Algorithm.Interop.Environment.InstallEnvironment();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "In Helper.InitEnvironment()");
                    }
                }).Start();
            #endregion
        }

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static void InitDataBase()
        {
            try
            {
                using var db
                    = new LiteDatabase(Path.GetFullPath(GlobalInfo.ActivitiesDataBaseFilePath));
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
                    activity.ID = Program.Config.Activity.TotalRecorded + 1;
                }), x => x.ID);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "In Helper.InitDataBase()");
            }
        }

        /// <summary>
        /// 记录活动
        /// </summary>
        /// <param name="activity">活动</param>
        /// <param name="colName">集合名称</param>
        /// <param name="action">添加后行动</param>
        /// <param name="keySelector">键选择器</param>
        public static void RecordActivity(Activity activity, string colName,
            Action action, Expression<Func<Activity, int>> keySelector)
        {
            lock (_activityRecordLock)
            {
                var col = Program.ActivitiesDataBase.GetCollection<Activity>(colName);
                col.Insert(activity);
                action();
                col.Update(activity);
                col.EnsureIndex(keySelector);
                Program.Config.Activity.TotalRecorded += 1;
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
                    throw new Exception("Plugin Package Doesn't Exist.");
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
    }
}

#pragma warning restore CS8601 // 引用类型赋值可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。

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

