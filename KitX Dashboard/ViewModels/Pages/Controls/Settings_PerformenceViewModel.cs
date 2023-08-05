using Avalonia;
using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Data;
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
using System.Linq;
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

    private void InitCommands()
    {
        EmptyLogsCommand = ReactiveCommand.Create(() =>
        {
            var location = $"{nameof(Settings_PerformenceViewModel)}.{nameof(EmptyLogsCommand)}";

            Task.Run(() =>
            {
                var dir = new DirectoryInfo(
                    ConfigManager.AppConfig.Log.LogFilePath.GetFullPath()
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

    private void InitEvents()
    {
        EventService.LogConfigUpdated += () =>
        {
            var logdir = ConfigManager.AppConfig.Log.LogFilePath.GetFullPath();

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

        EventService.DevicesServerPortChanged += () => PropertyChanged?.Invoke(
            this,
            new(nameof(DevicesServerPort))
        );

        EventService.PluginsServerPortChanged += () => PropertyChanged?.Invoke(
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

    internal static int DelayedWebStartSeconds
    {
        get => ConfigManager.AppConfig.Web.DelayStartSeconds;
        set
        {
            ConfigManager.AppConfig.Web.DelayStartSeconds = value;
            SaveAppConfigChanges();
        }
    }

    internal bool PluginsServerPortEditable => PluginsServerPortType != 0;

    internal int PluginsServerPortType
    {
        get => ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort is null ? 0 : 1;
        set
        {
            if (value == 0)
                ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort = null;
            else
                ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort = PluginsServerPort;

            PropertyChanged?.Invoke(
                this,
                new(nameof(PluginsServerPortEditable))
            );

            SaveAppConfigChanges();
        }
    }

    internal static int PluginsServerPort
    {
        get => GlobalInfo.PluginServerPort;
        set
        {
            if (value >= 0 && value <= 65535)
                ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort = value;
        }
    }

    internal static int DevicesServerPort => GlobalInfo.DeviceServerPort;

    internal static string LocalIPFilter
    {
        get => ConfigManager.AppConfig.Web.IPFilter;
        set
        {
            ConfigManager.AppConfig.Web.IPFilter = value;
            SaveAppConfigChanges();
        }
    }

    internal static string AcceptedNetworkInterfacesNames
    {
        get
        {
            var userPointed = ConfigManager.AppConfig.Web.AcceptedNetworkInterfaces;

            if (userPointed is null)
                return "Auto";
            else
                return userPointed.ToCustomString(";");
        }
        set
        {
            if (value.ToLower().Equals("auto"))
                ConfigManager.AppConfig.Web.AcceptedNetworkInterfaces = null;
            else
            {
                var userInput = value.Split(';');

                ConfigManager.AppConfig.Web.AcceptedNetworkInterfaces = userInput.ToList();
            }
        }
    }

    internal static ObservableCollection<string>? AvailableNetworkInterfaces
        => Instances.WebManager?.NetworkInterfaceRegistered;

    internal static ObservableCollection<string>? SelectedNetworkInterfaces { get; }
        = new();

    internal static int DevicesListRefreshDelay
    {
        get => ConfigManager.AppConfig.Web.DevicesViewRefreshDelay;
        set
        {
            ConfigManager.AppConfig.Web.DevicesViewRefreshDelay = value;
            SaveAppConfigChanges();
        }
    }

    internal static int GreetingTextUpdateInterval
    {
        get => ConfigManager.AppConfig.Windows.MainWindow.GreetingUpdateInterval;
        set
        {
            ConfigManager.AppConfig.Windows.MainWindow.GreetingUpdateInterval = value;
            EventService.Invoke(nameof(EventService.GreetingTextIntervalUpdated));
            SaveAppConfigChanges();
        }
    }

    internal static bool WebRelatedAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.WebRelatedAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.WebRelatedAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static bool WebRelatedAreaOfNetworkInterfacesExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static bool LogRelatedAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.LogRelatedAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.LogRelatedAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static bool UpdateRelatedAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static int LogFileSizeUsage
        => (int)(ConfigManager.AppConfig.Log.LogFilePath.GetTotalSize() / 1000 / 1024);

    internal static int LogFileSizeLimit
    {
        get => (int)(ConfigManager.AppConfig.Log.LogFileSingleMaxSize / 1024 / 1024);
        set
        {
            ConfigManager.AppConfig.Log.LogFileSingleMaxSize = value * 1024 * 1024;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveAppConfigChanges();
        }
    }

    internal static int LogFileMaxCount
    {
        get => ConfigManager.AppConfig.Log.LogFileMaxCount;
        set
        {
            ConfigManager.AppConfig.Log.LogFileMaxCount = value;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveAppConfigChanges();
        }
    }

    internal static int LogFileFlushInterval
    {
        get => ConfigManager.AppConfig.Log.LogFileFlushInterval;
        set
        {
            ConfigManager.AppConfig.Log.LogFileFlushInterval = value;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveAppConfigChanges();
        }
    }

    internal static int CheckerPerThreadFilesCountLimit
    {
        get => ConfigManager.AppConfig.IO.UpdatingCheckPerThreadFilesCount;
        set
        {
            ConfigManager.AppConfig.IO.UpdatingCheckPerThreadFilesCount = value;
            SaveAppConfigChanges();
        }
    }

    private static string GetLogLevelDisplayText(string key) => FetchStringFromResource(
        Application.Current,
        key,
        prefix: "Text_Log_"
    ) ?? string.Empty;

    internal static List<SupportedLogLevel> SupportedLogLevels { get; } = new()
    {
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
    };

    private SupportedLogLevel? _currentLogLevel = SupportedLogLevels.Find(
        x => x.LogEventLevel == ConfigManager.AppConfig.Log.LogLevel
    );

    internal SupportedLogLevel? CurrentLogLevel
    {
        get => _currentLogLevel;
        set
        {
            _currentLogLevel = value;

            if (value is not null)
            {
                ConfigManager.AppConfig.Log.LogLevel = value.LogEventLevel;

                EventService.Invoke(nameof(EventService.LogConfigUpdated));

                SaveAppConfigChanges();
            }
        }
    }

    internal ReactiveCommand<Unit, Unit>? EmptyLogsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RefreshLogsUsageCommand { get; set; }
}
