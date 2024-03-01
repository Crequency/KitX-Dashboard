using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Models;
using KitX.Dashboard.Names;
using KitX.Dashboard.Services;
using ReactiveUI;
using Serilog;
using Serilog.Events;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_PerformenceViewModel : ViewModelBase
{
    internal Settings_PerformenceViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        EmptyLogsCommand = ReactiveCommand.Create(() =>
        {
            var location = $"{nameof(Settings_PerformenceViewModel)}.{nameof(EmptyLogsCommand)}";

            Task.Run(() =>
            {
                var dir = new DirectoryInfo(
                    AppConfig.Log.LogFilePath.GetFullPath()
                );

                foreach (var file in dir.GetFiles())
                {
                    try
                    {
                        File.Delete(file.FullName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"In {location}: {ex.Message}");
                    }
                }

                this.RaisePropertyChanged(nameof(LogFileSizeUsage));
            });
        });

        RefreshLogsUsageCommand = ReactiveCommand.Create(
            () => this.RaisePropertyChanged(nameof(LogFileSizeUsage))
        );
    }

    public override void InitEvents()
    {
        EventService.LogConfigUpdated += () =>
        {
            var logdir = AppConfig.Log.LogFilePath.GetFullPath();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    $"{logdir}Log_.log",
                    outputTemplate: AppConfig.Log.LogTemplate,
                    rollingInterval: RollingInterval.Hour,
                    fileSizeLimitBytes: AppConfig.Log.LogFileSingleMaxSize,
                    buffered: true,
                    flushToDiskInterval: new(
                        0,
                        0,
                        AppConfig.Log.LogFileFlushInterval
                    ),
                    restrictedToMinimumLevel: AppConfig.Log.LogLevel,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: AppConfig.Log.LogFileMaxCount
                )
                .CreateLogger();
        };

        EventService.LanguageChanged += () =>
        {
            foreach (var item in SupportedLogLevels)
                item.LogLevelDisplayName = GetLogLevelDisplayText(item.LogLevelName ?? "");

            this.RaisePropertyChanged(nameof(SupportedLogLevels));
        };

        EventService.DevicesServerPortChanged += _ => this.RaisePropertyChanged(nameof(DevicesServerPort));

        EventService.PluginsServerPortChanged += _ => this.RaisePropertyChanged(nameof(PluginsServerPort));

        Instances.SignalTasksManager?.SignalRun(
            nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal),
            () =>
            {
                this.RaisePropertyChanged(nameof(AvailableNetworkInterfaces));

                Dispatcher.UIThread.Post(() =>
                {
                    SelectedNetworkInterfaces?.Clear();

                    var anin = AcceptedNetworkInterfacesNames;

                    if (anin is null || anin.Equals("Auto")) return;

                    if (AvailableNetworkInterfaces is null) return;

                    foreach (var item in anin.Split(';'))
                        if (AvailableNetworkInterfaces.Contains(item))
                            SelectedNetworkInterfaces?.Add(item);
                });
            },
            reusable: true
        );

        if (SelectedNetworkInterfaces is not null)
            SelectedNetworkInterfaces.CollectionChanged += (_, _) =>
            {
                if (SelectedNetworkInterfaces.Count == 0)
                    AcceptedNetworkInterfacesNames = "Auto";
                else
                {
                    var sb = new StringBuilder();

                    foreach (var adapter in SelectedNetworkInterfaces)
                    {
                        sb.Append(adapter);
                        sb.Append(';');
                    }

                    AcceptedNetworkInterfacesNames = sb.ToString()[..^1];
                }

                this.RaisePropertyChanged(nameof(AcceptedNetworkInterfacesNames));

                SaveAppConfigChanges();
            };
    }

    internal static double DelayedWebStartSeconds
    {
        get => AppConfig.Web.DelayStartSeconds;
        set
        {
            AppConfig.Web.DelayStartSeconds = value;
            SaveAppConfigChanges();
        }
    }

    internal bool PluginsServerPortEditable => PluginsServerPortType != 0;

    internal int PluginsServerPortType
    {
        get => AppConfig.Web.UserSpecifiedPluginsServerPort is null ? 0 : 1;
        set
        {
            if (value == 0)
                AppConfig.Web.UserSpecifiedPluginsServerPort = null;
            else
                AppConfig.Web.UserSpecifiedPluginsServerPort = PluginsServerPort;

            this.RaisePropertyChanged(nameof(PluginsServerPortEditable));

            SaveAppConfigChanges();
        }
    }

    internal static int PluginsServerPort
    {
        get => ConstantTable.PluginsServerPort;
        set
        {
            if (value >= 0 && value <= 65535)
                AppConfig.Web.UserSpecifiedPluginsServerPort = value;
        }
    }

    internal static int DevicesServerPort => ConstantTable.DevicesServerPort;

    internal static string LocalIPFilter
    {
        get => AppConfig.Web.IPFilter;
        set
        {
            AppConfig.Web.IPFilter = value;

            SaveAppConfigChanges();
        }
    }

    internal static string AcceptedNetworkInterfacesNames
    {
        get
        {
            var userPointed = AppConfig.Web.AcceptedNetworkInterfaces;

            if (userPointed is null)
                return "Auto";
            else
                return userPointed.ToCustomString(";");
        }
        set
        {
            if (value.ToLower().Equals("auto"))
                AppConfig.Web.AcceptedNetworkInterfaces = null;
            else
            {
                var userInput = value.Split(';');

                AppConfig.Web.AcceptedNetworkInterfaces = [.. userInput];
            }
        }
    }

    internal static ObservableCollection<string>? AvailableNetworkInterfaces => Instances.WebManager?.NetworkInterfaceRegistered;

    internal static ObservableCollection<string>? SelectedNetworkInterfaces { get; } = [];

    internal static int DevicesListRefreshDelay
    {
        get => AppConfig.Web.DevicesViewRefreshDelay;
        set
        {
            AppConfig.Web.DevicesViewRefreshDelay = value;

            SaveAppConfigChanges();
        }
    }

    internal static int GreetingTextUpdateInterval
    {
        get => AppConfig.Windows.MainWindow.GreetingUpdateInterval;
        set
        {
            AppConfig.Windows.MainWindow.GreetingUpdateInterval = value;

            EventService.Invoke(nameof(EventService.GreetingTextIntervalUpdated));

            SaveAppConfigChanges();
        }
    }

    internal static bool WebRelatedAreaExpanded
    {
        get => AppConfig.Pages.Settings.WebRelatedAreaExpanded;
        set
        {
            AppConfig.Pages.Settings.WebRelatedAreaExpanded = value;

            SaveAppConfigChanges();
        }
    }

    internal static bool WebRelatedAreaOfNetworkInterfacesExpanded
    {
        get => AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded;
        set
        {
            AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded = value;

            SaveAppConfigChanges();
        }
    }

    internal static bool LogRelatedAreaExpanded
    {
        get => AppConfig.Pages.Settings.LogRelatedAreaExpanded;
        set
        {
            AppConfig.Pages.Settings.LogRelatedAreaExpanded = value;

            SaveAppConfigChanges();
        }
    }

    internal static bool UpdateRelatedAreaExpanded
    {
        get => AppConfig.Pages.Settings.UpdateRelatedAreaExpanded;
        set
        {
            AppConfig.Pages.Settings.UpdateRelatedAreaExpanded = value;

            SaveAppConfigChanges();
        }
    }

    internal static int LogFileSizeUsage
        => (int)(AppConfig.Log.LogFilePath.GetTotalSize() / 1000 / 1024);

    internal static int LogFileSizeLimit
    {
        get => (int)(AppConfig.Log.LogFileSingleMaxSize / 1024 / 1024);
        set
        {
            AppConfig.Log.LogFileSingleMaxSize = value * 1024 * 1024;

            EventService.Invoke(nameof(EventService.LogConfigUpdated));

            SaveAppConfigChanges();
        }
    }

    internal static int LogFileMaxCount
    {
        get => AppConfig.Log.LogFileMaxCount;
        set
        {
            AppConfig.Log.LogFileMaxCount = value;

            EventService.Invoke(nameof(EventService.LogConfigUpdated));

            SaveAppConfigChanges();
        }
    }

    internal static int LogFileFlushInterval
    {
        get => AppConfig.Log.LogFileFlushInterval;
        set
        {
            AppConfig.Log.LogFileFlushInterval = value;

            EventService.Invoke(nameof(EventService.LogConfigUpdated));

            SaveAppConfigChanges();
        }
    }

    internal static int CheckerPerThreadFilesCountLimit
    {
        get => AppConfig.IO.UpdatingCheckPerThreadFilesCount;
        set
        {
            AppConfig.IO.UpdatingCheckPerThreadFilesCount = value;

            SaveAppConfigChanges();
        }
    }

    private static string GetLogLevelDisplayText(string key) => Translate(key, prefix: "Text_Log_") ?? string.Empty;

    internal static List<SupportedLogLevel> SupportedLogLevels { get; } =
    [
        new()
        {
            LogEventLevel = LogEventLevel.Verbose,
            LogLevelName = "Verbose",
            LogLevelDisplayName = GetLogLevelDisplayText("Verbose")
        },
        new()
        {
            LogEventLevel = LogEventLevel.Debug,
            LogLevelName = "Debug",
            LogLevelDisplayName = GetLogLevelDisplayText("Debug")
        },
        new()
        {
            LogEventLevel = LogEventLevel.Information,
            LogLevelName = "Information",
            LogLevelDisplayName = GetLogLevelDisplayText("Information")
        },
        new()
        {
            LogEventLevel = LogEventLevel.Warning,
            LogLevelName = "Warning",
            LogLevelDisplayName = GetLogLevelDisplayText("Warning")
        },
        new()
        {
            LogEventLevel = LogEventLevel.Error,
            LogLevelName = "Error",
            LogLevelDisplayName = GetLogLevelDisplayText("Error")
        },
        new()
        {
            LogEventLevel = LogEventLevel.Fatal,
            LogLevelName = "Fatal",
            LogLevelDisplayName = GetLogLevelDisplayText("Fatal")
        },
    ];

    private SupportedLogLevel? _currentLogLevel = SupportedLogLevels.Find(
        x => x.LogEventLevel == AppConfig.Log.LogLevel
    );

    internal SupportedLogLevel? CurrentLogLevel
    {
        get => _currentLogLevel;
        set
        {
            _currentLogLevel = value;

            if (value is not null)
            {
                AppConfig.Log.LogLevel = value.LogEventLevel;

                EventService.Invoke(nameof(EventService.LogConfigUpdated));

                SaveAppConfigChanges();
            }
        }
    }

    internal ReactiveCommand<Unit, Unit>? EmptyLogsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RefreshLogsUsageCommand { get; set; }
}
