using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommandLine;
using Common.BasicHelper.IO;
using Common.BasicHelper.Utils.Extensions;
using KitX.Core.Activity;
using KitX.Core.Configuration;
using KitX.Core.Contract.Activity;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Statistics;
using KitX.Core.Plugin;
using KitX.Core.Statistics;
using KitX.Core.Tasks;
using KitX.Dashboard.Names;
using KitX.Dashboard.Options;
using KitX.Dashboard.Services;
using LiteDB;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard;

public static class AppFramework
{
    private static readonly Queue<Action> actionsInInitialization = [];

    public static void ProcessStartupArguments()
    {
        Parser
            .Default.ParseArguments<StartupOptions>(Environment.GetCommandLineArgs())
            .WithParsed(opt =>
            {
                ConstantTable.IsSingleProcessStartMode = !opt.DisableSingleProcessCheck;
                ConstantTable.EnabledConfigFileHotReload = !opt.DisableConfigHotReload;
                ConstantTable.SkipNetworkSystemOnStartup = opt.DisableNetworkSystemOnStartup;

                TasksManager.RunTask(
                    () =>
                    {
                        if (opt.PluginPath is not null)
                            ImportPlugin(opt.PluginPath);
                    },
                    $"{nameof(ImportPlugin)}",
                    catchException: true
                );
            });
    }

    public static void RunFramework()
    {
        if (Design.IsDesignMode)
            return;

        // Step 1: Load configuration from ConfigManager singleton
        var configService = ConfigManager.Instance;
        configService.Load();
        var config = configService.TypedAppConfig;

        // Step 2: Initialize log system before any DI container operations
        // Logger doesn't depend on DI container, only on ConfigManager singleton
        var logdir = config.Log.LogFilePath.GetFullPath();

        if (!Directory.Exists(logdir))
            Directory.CreateDirectory(logdir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: config.Log.LogTemplate, restrictedToMinimumLevel: config.Log.LogLevel)
            .WriteTo.File(
                $"{logdir}Log_.log",
                outputTemplate: config.Log.LogTemplate,
                rollingInterval: RollingInterval.Hour,
                fileSizeLimitBytes: config.Log.LogFileSingleMaxSize,
                buffered: true,
                flushToDiskInterval: new(0, 0, config.Log.LogFileFlushInterval),
                restrictedToMinimumLevel: config.Log.LogLevel,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: config.Log.LogFileMaxCount
            )
            .CreateLogger();

        Log.Information("KitX Dashboard Started.");

        // Step 3: Initialize DI container (Logger is now available)
        App.InitializeServiceProvider();

        // If dump file exists, delete it.
        if (File.Exists("./dump.log".GetFullPath()))
            File.Delete("./dump.log".GetFullPath());

        if (File.Exists("restart.lock"))
        {
            var waitCount = 0;

            while (Process.GetProcesses().Count(x => x.ProcessName.StartsWith("KitX.Dashboard")) >= 2)
            {
                if (waitCount > 10)
                    Environment.Exit(ExitCodes.WaitRestartingLockFileTooLong);

                ++waitCount;

                Thread.Sleep(1000);
            }

            File.Delete("restart.lock");
        }

        configService.TypedAppConfig.App.RanTime++;

        ProcessStartupArguments();

        if (ConstantTable.IsSingleProcessStartMode)
            Process
                .GetProcesses()
                .WhenCount(
                    count => count >= 2,
                    item => item.ProcessName.StartsWith("KitX.Dashboard"),
                    _ => Environment.Exit(ExitCodes.MultiProcessesStarted)
                );

        LoadResource();

        Log.Information("Calling Instances.Initialize()...");
        Instances.Initialize();
        Log.Information("Instances.Initialize() completed.");

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

        Instances.SignalTasksManager!.SignalRun(
            nameof(SignalsNames.MainWindowInitSignal),
            () =>
            {
                new Thread(async () =>
                {
                    Thread.Sleep(Convert.ToInt32(config.Web.DelayStartSeconds * 1000));

                    if (!ConstantTable.SkipNetworkSystemOnStartup)
                    {
                        // Use DI services instead of WebManager
                        var discoveryServer = Instances.DevicesDiscoveryServer;
                        var devicesServer = Instances.DevicesServer;
                        var pluginsServer = Instances.PluginsServer;

                        if (discoveryServer != null)
                        {
                            discoveryServer.ConfigurePort((int)(config.Web.UserSpecifiedDevicesServerPort ?? 0));
                            discoveryServer.Run();
                            KitX.Core.Device.DevicesOrganizer.Run();
                        }

                        if (devicesServer != null)
                        {
                            devicesServer.ConfigurePort((int)(config.Web.UserSpecifiedPluginsServerPort ?? 0));
                            devicesServer.Run();
                        }

                        if (pluginsServer != null)
                        {
                            pluginsServer.Run();
                        }
                    }
                }).Start();
            }
        );

        #endregion

        #region Initialize StatisticsManager

        App.GetService<IStatisticsService>().Start();

        #endregion

        #region Initialize persistent windows

        Instances.SignalTasksManager.SignalRun(
            nameof(SignalsNames.MainWindowInitSignal),
            () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UIStateService.PluginsLaunchWindow = new();
                });
            }
        );

        #endregion

        actionsInInitialization.ForEach(x => x.Invoke());
    }

    private static void InitDataBase()
    {
        const string location = $"{nameof(AppFramework)}.{nameof(InitDataBase)}";

        try
        {
            var dir = ConstantTable.DataPath.GetFullPath();

            if (!Directory.Exists(dir))
                _ = Directory.CreateDirectory(dir);

            var dbfile = ConstantTable.ActivitiesDataBaseFilePath.GetFullPath();

            var db = new LiteDatabase(dbfile);

            Instances.ActivitiesDataBase = db;

            // Also set the database for Core ActivityManager
            KitX.Core.Activity.ActivityManager.ActivitiesDatabase = db;

            App.GetService<IActivityService>().RecordAppStart();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    private static async void LoadResource()
    {
        const string location = $"{nameof(AppFramework)}.{nameof(LoadResource)}";

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
        const string location = $"{nameof(AppFramework)}.{nameof(ImportPlugin)}";

        try
        {
            if (!File.Exists(kxpPath))
            {
                Console.WriteLine($"No this file: {kxpPath}");

                throw new Exception("Plugins Package Doesn't Exist.");
            }
            else
            {
                _ = App.GetService<IPluginService>().ImportPluginAsync(kxpPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
    }

    public static void EnsureExit()
    {
        if (Design.IsDesignMode)
            return;

        const string location = $"{nameof(AppFramework)}.{nameof(EnsureExit)}";

        ConstantTable.EnsureExiting = true;

        new Thread(async () =>
        {
            try
            {
                App.GetService<IActivityService>().RecordAppExit();

                Instances.FileWatcherService?.Clear();

                App.GetService<IConfigService>().SaveAll();

                Log.CloseAndFlush();

                // Use DI services instead of WebManager
                var pluginsServer = Instances.PluginsServer;
                var devicesDiscoveryServer = Instances.DevicesDiscoveryServer;
                var devicesServer = Instances.DevicesServer;

                pluginsServer?.Stop();
                devicesServer?.Stop();
                devicesDiscoveryServer?.Stop();

                Instances.ActivitiesDataBase?.Commit();
                Instances.ActivitiesDataBase?.Dispose();

                ConstantTable.Running = false;

                if (ConstantTable.Restarting)
                {
                    File.WriteAllText("restart.lock", "Program is restarting, please stand by ...");

                    var path = Process.GetCurrentProcess().MainModule?.FileName;

                    if (path is not null)
                        Process.Start(path);
                }

                Thread.Sleep(App.GetService<IConfigService>().AppConfig.App.LastBreakAfterExit);

                ConstantTable.EnsureExiting = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        }).Start();

        while (ConstantTable.EnsureExiting)
            ;

        Environment.Exit(0);
    }
}
