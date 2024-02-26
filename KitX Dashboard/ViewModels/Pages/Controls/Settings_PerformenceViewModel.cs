using Avalonia;
using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Models;
using KitX.Dashboard.Names;
using KitX.Dashboard.Services;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_PerformenceViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

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
                    ConfigManager.Instance.AppConfig.Log.LogFilePath.GetFullPath()
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

                PropertyChanged?.Invoke(this,
                    new(nameof(LogFileSizeUsage))
                );
            });
        });

        RefreshLogsUsageCommand = ReactiveCommand.Create(
            () => PropertyChanged?.Invoke(
                this,
                new(nameof(LogFileSizeUsage))
            )
        );
    }

    public override void InitEvents()
    {
        EventService.LogConfigUpdated += () =>
        {
            var logdir = ConfigManager.Instance.AppConfig.Log.LogFilePath.GetFullPath();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    $"{logdir}Log_.log",
                    outputTemplate: ConfigManager.Instance.AppConfig.Log.LogTemplate,
                    rollingInterval: RollingInterval.Hour,
                    fileSizeLimitBytes: ConfigManager.Instance.AppConfig.Log.LogFileSingleMaxSize,
                    buffered: true,
                    flushToDiskInterval: new(
                        0,
                        0,
                        ConfigManager.Instance.AppConfig.Log.LogFileFlushInterval
                    ),
                    restrictedToMinimumLevel: ConfigManager.Instance.AppConfig.Log.LogLevel,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: ConfigManager.Instance.AppConfig.Log.LogFileMaxCount
                )
                .CreateLogger();
        };

        EventService.LanguageChanged += () =>
        {
            foreach (var item in SupportedLogLevels)
                item.LogLevelDisplayName = GetLogLevelDisplayText(item.LogLevelName ?? "");

            PropertyChanged?.Invoke(
                this,
                new(nameof(SupportedLogLevels))
            );
        };

        EventService.DevicesServerPortChanged += _ => PropertyChanged?.Invoke(
            this,
            new(nameof(DevicesServerPort))
        );

        EventService.PluginsServerPortChanged += _ => PropertyChanged?.Invoke(
            this,
            new(nameof(PluginsServerPort))
        );

        Instances.SignalTasksManager?.SignalRun(
            nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal),
            () =>
            {
                PropertyChanged?.Invoke(
                    this,
                    new(nameof(AvailableNetworkInterfaces))
                );

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

                PropertyChanged?.Invoke(
                    this,
                    new(nameof(AcceptedNetworkInterfacesNames))
                );

                SaveAppConfigChanges();
            };
    }

    internal static double DelayedWebStartSeconds
    {
        get => ConfigManager.Instance.AppConfig.Web.DelayStartSeconds;
        set
        {
            ConfigManager.Instance.AppConfig.Web.DelayStartSeconds = value;
            SaveAppConfigChanges();
        }
    }

    internal bool PluginsServerPortEditable => PluginsServerPortType != 0;

    internal int PluginsServerPortType
    {
        get => ConfigManager.Instance.AppConfig.Web.UserSpecifiedPluginsServerPort is null ? 0 : 1;
        set
        {
            if (value == 0)
                ConfigManager.Instance.AppConfig.Web.UserSpecifiedPluginsServerPort = null;
            else
                ConfigManager.Instance.AppConfig.Web.UserSpecifiedPluginsServerPort = PluginsServerPort;

            PropertyChanged?.Invoke(
                this,
                new(nameof(PluginsServerPortEditable))
            );

            SaveAppConfigChanges();
        }
    }

    internal static int PluginsServerPort
    {
        get => ConstantTable.PluginsServerPort;
        set
        {
            if (value >= 0 && value <= 65535)
                ConfigManager.Instance.AppConfig.Web.UserSpecifiedPluginsServerPort = value;
        }
    }

    internal static int DevicesServerPort => ConstantTable.DevicesServerPort;

    internal static string LocalIPFilter
    {
        get => ConfigManager.Instance.AppConfig.Web.IPFilter;
        set
        {
            ConfigManager.Instance.AppConfig.Web.IPFilter = value;
            SaveAppConfigChanges();
        }
    }

    internal static string AcceptedNetworkInterfacesNames
    {
        get
        {
            var userPointed = ConfigManager.Instance.AppConfig.Web.AcceptedNetworkInterfaces;

            if (userPointed is null)
                return "Auto";
            else
                return userPointed.ToCustomString(";");
        }
        set
        {
            if (value.ToLower().Equals("auto"))
                ConfigManager.Instance.AppConfig.Web.AcceptedNetworkInterfaces = null;
            else
            {
                var userInput = value.Split(';');

                ConfigManager.Instance.AppConfig.Web.AcceptedNetworkInterfaces = [.. userInput];
            }
        }
    }

    internal static ObservableCollection<string>? AvailableNetworkInterfaces => Instances.WebManager?.NetworkInterfaceRegistered;

    internal static ObservableCollection<string>? SelectedNetworkInterfaces { get; } = [];

    internal static int DevicesListRefreshDelay
    {
        get => ConfigManager.Instance.AppConfig.Web.DevicesViewRefreshDelay;
        set
        {
            ConfigManager.Instance.AppConfig.Web.DevicesViewRefreshDelay = value;
            SaveAppConfigChanges();
        }
    }

    internal static int GreetingTextUpdateInterval
    {
        get => ConfigManager.Instance.AppConfig.Windows.MainWindow.GreetingUpdateInterval;
        set
        {
            ConfigManager.Instance.AppConfig.Windows.MainWindow.GreetingUpdateInterval = value;
            EventService.Invoke(nameof(EventService.GreetingTextIntervalUpdated));
            SaveAppConfigChanges();
        }
    }

    internal static bool WebRelatedAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.WebRelatedAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.WebRelatedAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static bool WebRelatedAreaOfNetworkInterfacesExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static bool LogRelatedAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.LogRelatedAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.LogRelatedAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static bool UpdateRelatedAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static int LogFileSizeUsage
        => (int)(ConfigManager.Instance.AppConfig.Log.LogFilePath.GetTotalSize() / 1000 / 1024);

    internal static int LogFileSizeLimit
    {
        get => (int)(ConfigManager.Instance.AppConfig.Log.LogFileSingleMaxSize / 1024 / 1024);
        set
        {
            ConfigManager.Instance.AppConfig.Log.LogFileSingleMaxSize = value * 1024 * 1024;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveAppConfigChanges();
        }
    }

    internal static int LogFileMaxCount
    {
        get => ConfigManager.Instance.AppConfig.Log.LogFileMaxCount;
        set
        {
            ConfigManager.Instance.AppConfig.Log.LogFileMaxCount = value;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveAppConfigChanges();
        }
    }

    internal static int LogFileFlushInterval
    {
        get => ConfigManager.Instance.AppConfig.Log.LogFileFlushInterval;
        set
        {
            ConfigManager.Instance.AppConfig.Log.LogFileFlushInterval = value;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveAppConfigChanges();
        }
    }

    internal static int CheckerPerThreadFilesCountLimit
    {
        get => ConfigManager.Instance.AppConfig.IO.UpdatingCheckPerThreadFilesCount;
        set
        {
            ConfigManager.Instance.AppConfig.IO.UpdatingCheckPerThreadFilesCount = value;
            SaveAppConfigChanges();
        }
    }

    private static string GetLogLevelDisplayText(string key) => Translate(key, prefix: "Text_Log_") ?? string.Empty;

    internal static List<SupportedLogLevel> SupportedLogLevels { get; } =
    [
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Verbose,
            LogLevelName = "Verbose",
            LogLevelDisplayName = GetLogLevelDisplayText("Verbose")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Debug,
            LogLevelName = "Debug",
            LogLevelDisplayName = GetLogLevelDisplayText("Debug")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Information,
            LogLevelName = "Information",
            LogLevelDisplayName = GetLogLevelDisplayText("Information")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Warning,
            LogLevelName = "Warning",
            LogLevelDisplayName = GetLogLevelDisplayText("Warning")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Error,
            LogLevelName = "Error",
            LogLevelDisplayName = GetLogLevelDisplayText("Error")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Fatal,
            LogLevelName = "Fatal",
            LogLevelDisplayName = GetLogLevelDisplayText("Fatal")
        },
    ];

    private SupportedLogLevel? _currentLogLevel = SupportedLogLevels.Find(
        x => x.LogEventLevel == ConfigManager.Instance.AppConfig.Log.LogLevel
    );

    internal SupportedLogLevel? CurrentLogLevel
    {
        get => _currentLogLevel;
        set
        {
            _currentLogLevel = value;

            if (value is not null)
            {
                ConfigManager.Instance.AppConfig.Log.LogLevel = value.LogEventLevel;

                EventService.Invoke(nameof(EventService.LogConfigUpdated));

                SaveAppConfigChanges();
            }
        }
    }

    internal ReactiveCommand<Unit, Unit>? EmptyLogsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RefreshLogsUsageCommand { get; set; }
}
