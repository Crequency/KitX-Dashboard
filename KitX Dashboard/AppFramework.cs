using Avalonia.Threading;
using CommandLine;
using Common.BasicHelper.IO;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Names;
using KitX.Dashboard.Options;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using LiteDB;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace KitX.Dashboard;

public static class AppFramework
{
    private readonly static Queue<Action> actionsInInitialization = [];

    public static void ProcessStartupArguments()
    {
        Parser.Default.ParseArguments<StartupOptions>(Environment.GetCommandLineArgs())
            .WithParsed(opt =>
            {
                ConstantTable.IsSingleProcessStartMode = !opt.DisableSingleProcessCheck;
                ConstantTable.EnabledConfigFileHotReload = !opt.DisableConfigHotReload;
                ConstantTable.SkipNetworkSystemOnStartup = opt.DisableNetworkSystemOnStartup;

                TasksManager.RunTask(() =>
                {
                    if (opt.PluginPath is not null)
                        ImportPlugin(opt.PluginPath);
                }, $"{nameof(ImportPlugin)}", catchException: true);
            });
    }

    public static void RunFramework()
    {
        // If dump file exists, delete it.
        if (File.Exists("./dump.log".GetFullPath()))
            File.Delete("./dump.log".GetFullPath());

        EventService.Initialize();

        Instances.Initialize();

        Instances.ConfigManager.AppConfig.App.RanTime++;

        var config = Instances.ConfigManager.AppConfig;

        ProcessStartupArguments();

        if (ConstantTable.IsSingleProcessStartMode)
            Process.GetProcesses().WhenCount(
                count => count >= 2,
                item => item.ProcessName.StartsWith("KitX.Dashboard"),
                _ => Environment.Exit(ExitCodes.MultiProcessesStarted)
            );

        LoadResource();

        #region Initialize log system

        var logdir = config.Log.LogFilePath.GetFullPath();

        if (!Directory.Exists(logdir))
            Directory.CreateDirectory(logdir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                $"{logdir}Log_.log",
                outputTemplate: config.Log.LogTemplate,
                rollingInterval: RollingInterval.Hour,
                fileSizeLimitBytes: config.Log.LogFileSingleMaxSize,
                buffered: true,
                flushToDiskInterval: new(
                    0,
                    0,
                    config.Log.LogFileFlushInterval
                ),
                restrictedToMinimumLevel: config.Log.LogLevel,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: config.Log.LogFileMaxCount
            )
            .CreateLogger();

        Log.Information("KitX Dashboard Started.");

        #endregion

        #region Initialize global exception catching

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

        #region Initialize DataBase

        InitDataBase();

        #endregion

        #region Initialize WebManager

        Instances.SignalTasksManager!.SignalRun(nameof(SignalsNames.MainWindowInitSignal), () =>
        {
            new Thread(async () =>
            {
                Thread.Sleep(
                    Convert.ToInt32(config.Web.DelayStartSeconds * 1000)
                );

                if (ConstantTable.SkipNetworkSystemOnStartup)
                    Instances.WebManager = new();
                else
                    Instances.WebManager = await new WebManager().Start();
            }).Start();
        });

        #endregion

        #region Initialize StatisticsManager

        StatisticsManager.Start();

        #endregion

        #region Initialize persistent windows

        Instances.SignalTasksManager.SignalRun(nameof(SignalsNames.MainWindowInitSignal), () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ViewInstances.PluginsLaunchWindow = new();
            });
        });

        #endregion

        actionsInInitialization.ForEach(x => x.Invoke());
    }

    private static void InitDataBase()
    {
        var location = $"{nameof(AppFramework)}.{nameof(InitDataBase)}";

        try
        {
            var dir = ConstantTable.DataPath.GetFullPath();

            if (!Directory.Exists(dir)) _ = Directory.CreateDirectory(dir);

            var dbfile = ConstantTable.ActivitiesDataBaseFilePath.GetFullPath();

            var db = new LiteDatabase(dbfile);

            Instances.ActivitiesDataBase = db;

            ActivityManager.RecordAppStart();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    private static async void LoadResource()
    {
        var location = $"{nameof(AppFramework)}.{nameof(LoadResource)}";

        try
        {
            ConstantTable.KitXIconBase64 = await FileHelper.ReadAllAsync(
                $"{ConstantTable.AssetsPath}{ConstantTable.IconBase64FileName}".GetFullPath()
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    public static void AfterInitailization(Action action) => actionsInInitialization.Enqueue(action);

    private static void ImportPlugin(string kxpPath)
    {
        var location = $"{nameof(AppFramework)}.{nameof(ImportPlugin)}";

        try
        {
            if (!File.Exists(kxpPath))
            {
                Console.WriteLine($"No this file: {kxpPath}");

                throw new Exception("Plugins Package Doesn't Exist.");
            }
            else
            {
                PluginsManager.ImportPlugin([kxpPath]);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    public static void EnsureExit()
    {
        var location = $"{nameof(AppFramework)}.{nameof(EnsureExit)}";

        new Thread(() =>
        {
            try
            {
                ActivityManager.RecordAppExit();

                Instances.FileWatcherManager?.Clear();

                Instances.ConfigManager.SaveAll();

                Log.CloseAndFlush();

                Instances.WebManager?.Stop();
                Instances.WebManager?.Dispose();

                Instances.ActivitiesDataBase?.Commit();
                Instances.ActivitiesDataBase?.Dispose();

                ConstantTable.Running = false;

                Thread.Sleep(Instances.ConfigManager.AppConfig.App.LastBreakAfterExit);

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"In {location}: {ex.Message}");
            }
        }).Start();
    }
}
