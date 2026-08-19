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
using KitX.Core.Configuration;
using KitX.Core.Contract.Activity;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Statistics;
using KitX.Core.Plugin;
using KitX.Core.Statistics;
using KitX.Core.Contract.Tasks;
using KitX.Dashboard.Names;
using KitX.Dashboard.Options;
using KitX.Dashboard.Services;
using ReactiveUI;
using Serilog;
using Serilog.Events;
using System.Text.Json;

namespace KitX.Dashboard;

public static class AppFramework
{
    private static readonly Queue<Action> actionsInInitialization = [];

    /// <summary>
    /// Signal event for graceful exit — replaces busy-wait loop in EnsureExit.
    /// </summary>
    private static readonly ManualResetEventSlim _exitCompleteEvent = new(false);

    public static void ProcessStartupArguments()
    {
        Parser
            .Default.ParseArguments<StartupOptions>(Environment.GetCommandLineArgs())
            .WithParsed(opt =>
            {
                ConstantTable.IsSingleProcessStartMode = !opt.DisableSingleProcessCheck;
                ConstantTable.EnabledConfigFileHotReload = !opt.DisableConfigHotReload;
                ConstantTable.SkipNetworkSystemOnStartup = opt.DisableNetworkSystemOnStartup;

                App.GetService<ITasksService>().RunTask(
                    () =>
                    {
                        if (opt.PluginPath is not null)
                            ImportPlugin(opt.PluginPath);
                    },
                    taskName: $"{nameof(ImportPlugin)}"
                );
            });
    }

    public static void RunFramework()
    {
        if (Design.IsDesignMode)
            return;

        // Step 1: Initialize DI container first
        App.InitializeServiceProvider();

        // Step 2: Read LogLevel directly from config file (before full load,
        // so the logger can capture any deserialization errors during Load).
        var logLevel = LogEventLevel.Information;
        try
        {
            var cfgPath = Path.GetFullPath(Path.Combine("./Config/", "AppConfig.json"));
            if (File.Exists(cfgPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
                if (doc.RootElement.TryGetProperty("Log", out var log) &&
                    log.TryGetProperty("LogLevel", out var level))
                    logLevel = (LogEventLevel)level.GetInt32();
            }
        }
        catch { }

        // Step 3: Configure logger before Load() so Load errors are visible
        var logdir = "./Log/".GetFullPath();
        if (!Directory.Exists(logdir)) Directory.CreateDirectory(logdir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.File(
                $"{logdir}Log_.log",
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Hour,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                buffered: true,
                flushToDiskInterval: new(0, 0, 30),
                restrictedToMinimumLevel: logLevel,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 50
            )
            .CreateLogger();

        // Step 4: Full config load (with logger now active — errors are visible)
        Log.Information($"[AppFramework] About to call configService.Load(), temp LogLevel={logLevel}");
        var configService = App.GetService<IConfigService>();
        configService.Load();
        var config = configService.AppConfig;
        Log.Information($"[AppFramework] Load complete, LogLevel={config.Log.LogLevel}");

        // Step 5: Reconfigure logger with full settings from loaded config
        LoggerConfigurator.Configure(config.Log, writeToConsole: true);

        Log.Information("KitX Dashboard Started.");

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

        configService.AppConfig.App.RanTime++;

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

        // Database initialization now happens inside the Core ActivityManager constructor:
        // the DI singleton opens the LiteDB file on first IActivityService resolution, so no
        // Dashboard-side init is needed. Record the app start here.
        try
        {
            App.GetService<IActivityService>().RecordAppStart();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {nameof(AppFramework)}.RecordAppStart: {ex.Message}");
        }

        #region Initialize WebManager

        // Network startup is orchestrated by the Core-level INetworkService
        // (startup ordering, DelayStartSeconds, SkipNetworkSystemOnStartup and
        // port configuration all live in KitX.Core).
        var signalTasksManager = App.GetService<Common.BasicHelper.Core.TaskSystem.SignalTasksManager>();
        signalTasksManager.SignalRun(
            nameof(SignalsNames.MainWindowInitSignal),
            () =>
            {
                new Thread(async () =>
                {
                    try
                    {
                        await App.GetService<INetworkService>().StartAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"In {nameof(AppFramework)}.NetworkStartup: {ex.Message}");
                    }
                }).Start();
            }
        );

        #endregion

        #region Initialize StatisticsManager

        App.GetService<IStatisticsService>().Start();

        #endregion

        #region Initialize persistent windows

        signalTasksManager.SignalRun(
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

    public static void AfterInitialization(Action action) => actionsInInitialization.Enqueue(action);

    private static async void ImportPlugin(string kxpPath)
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
                await App.GetService<IPluginService>().ImportPluginAsync(kxpPath);
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
        _exitCompleteEvent.Reset();

        new Thread(async () =>
        {
            try
            {
                App.GetService<IActivityService>().RecordAppExit();

                App.GetService<KitX.Core.Contract.FileWatcher.IFileWatcherService>()?.Clear();

                App.GetService<IConfigService>().SaveAll();

                Log.CloseAndFlush();

                // Network shutdown is orchestrated by the Core-level INetworkService.
                await App.GetService<INetworkService>().StopAsync();

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
                _exitCompleteEvent.Set();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
                _exitCompleteEvent.Set();
            }
        }).Start();

        _exitCompleteEvent.Wait();

        Environment.Exit(0);
    }
}
