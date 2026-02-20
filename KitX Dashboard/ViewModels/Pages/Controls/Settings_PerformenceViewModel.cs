using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.Event;
using KitX.Dashboard;
using KitX.Dashboard.Models;
using KitX.Dashboard.Names;
using ReactiveUI;
using Serilog;
using Serilog.Events;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_PerformenceViewModel : ViewModelBase
{
    /// <summary>
    /// Gets the config service (for static properties access)
    /// </summary>
    private static IConfigService ConfigService => App.GetService<IConfigService>();

    internal Settings_PerformenceViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        EmptyLogsCommand = ReactiveCommand.Create(() =>
        {
            const string location = $"{nameof(Settings_PerformenceViewModel)}.{nameof(EmptyLogsCommand)}";

            Task.Run(() =>
            {
                var dir = new DirectoryInfo(ConfigService.AppConfig.Log.LogFilePath.GetFullPath());

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

        RefreshLogsUsageCommand = ReactiveCommand.Create(() => this.RaisePropertyChanged(nameof(LogFileSizeUsage)));
    }

    public sealed override void InitEvents()
    {
        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.LogConfigUpdated, (s, e) =>
        {
            var logdir = ConfigService.AppConfig.Log.LogFilePath.GetFullPath();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    $"{logdir}Log_.log",
                    outputTemplate: ConfigService.AppConfig.Log.LogTemplate,
                    rollingInterval: RollingInterval.Hour,
                    fileSizeLimitBytes: ConfigService.AppConfig.Log.LogFileSingleMaxSize,
                    buffered: true,
                    flushToDiskInterval: new(0, 0, ConfigService.AppConfig.Log.LogFileFlushInterval),
                    restrictedToMinimumLevel: ConfigService.AppConfig.Log.LogLevel,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: ConfigService.AppConfig.Log.LogFileMaxCount
                )
                .CreateLogger();
        });

        eventService.Subscribe(EventNames.LanguageChanged, (s, e) =>
        {
            foreach (var item in SupportedLogLevels)
                item.LogLevelDisplayName = GetLogLevelDisplayText(item.LogLevelName ?? "");

            this.RaisePropertyChanged(nameof(SupportedLogLevels));
        });

        eventService.Subscribe<PortChangedEventArgs>(EventNames.DevicesServerPortChanged, (s, e) => this.RaisePropertyChanged(nameof(DevicesServerPort)));

        eventService.Subscribe<PortChangedEventArgs>(EventNames.PluginsServerPortChanged, (s, e) => this.RaisePropertyChanged(nameof(PluginsServerPort)));

        Instances.SignalTasksManager?.SignalRun(
            nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal),
            () =>
            {
                this.RaisePropertyChanged(nameof(AvailableNetworkInterfaces));

                Dispatcher.UIThread.Post(() =>
                {
                    SelectedNetworkInterfaces?.Clear();

                    var anin = AcceptedNetworkInterfacesNames;

                    if (anin is null || anin.Equals("Auto"))
                        return;

                    if (AvailableNetworkInterfaces is null)
                        return;

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

                ConfigService.SaveAll();
            };
    }

    internal static double DelayedWebStartSeconds
    {
        get => ConfigService.AppConfig.Web.DelayStartSeconds;
        set
        {
            ConfigService.AppConfig.Web.DelayStartSeconds = value;
            ConfigService.SaveAll();
        }
    }

    internal bool PluginsServerPortEditable => PluginsServerPortType != 0;

    internal int PluginsServerPortType
    {
        get => ConfigService.AppConfig.Web.UserSpecifiedPluginsServerPort is null ? 0 : 1;
        set
        {
            if (value == 0)
                ConfigService.AppConfig.Web.UserSpecifiedPluginsServerPort = null;
            else
                ConfigService.AppConfig.Web.UserSpecifiedPluginsServerPort = PluginsServerPort;

            this.RaisePropertyChanged(nameof(PluginsServerPortEditable));

            ConfigService.SaveAll();
        }
    }

    internal static int PluginsServerPort
    {
        get => ConstantTable.PluginsServerPort;
        set
        {
            if (value >= 0 && value <= 65535)
                ConfigService.AppConfig.Web.UserSpecifiedPluginsServerPort = value;
        }
    }

    internal static int DevicesServerPort => ConstantTable.DevicesServerPort;

    internal static string LocalIPFilter
    {
        get => ConfigService.AppConfig.Web.IPFilter;
        set
        {
            ConfigService.AppConfig.Web.IPFilter = value;

            ConfigService.SaveAll();
        }
    }

    internal static string AcceptedNetworkInterfacesNames
    {
        get
        {
            var userPointed = ConfigService.AppConfig.Web.AcceptedNetworkInterfaces;

            if (userPointed is null)
                return "Auto";
            else
                return userPointed.ToCustomString(";");
        }
        set
        {
            if (value.ToLower().Equals("auto"))
                ConfigService.AppConfig.Web.AcceptedNetworkInterfaces = null;
            else
            {
                var userInput = value.Split(';');

                ConfigService.AppConfig.Web.AcceptedNetworkInterfaces = [.. userInput];
            }
        }
    }

    internal static ObservableCollection<string>? AvailableNetworkInterfaces =>
        new(NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                          (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                           nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
            .Select(x => x.Name));

    internal static ObservableCollection<string>? SelectedNetworkInterfaces { get; } = [];

    internal static int DevicesListRefreshDelay
    {
        get => ConfigService.AppConfig.Web.DevicesViewRefreshDelay;
        set
        {
            ConfigService.AppConfig.Web.DevicesViewRefreshDelay = value;

            ConfigService.SaveAll();
        }
    }

    internal static int GreetingTextUpdateInterval
    {
        get => ConfigService.AppConfig.Windows.MainWindow.GreetingUpdateInterval;
        set
        {
            ConfigService.AppConfig.Windows.MainWindow.GreetingUpdateInterval = value;

            var eventService = App.GetService<IEventService>();
            eventService.Publish(EventNames.GreetingTextIntervalUpdated, EventArgs.Empty);

            ConfigService.SaveAll();
        }
    }

    internal static bool WebRelatedAreaExpanded
    {
        get => ConfigService.AppConfig.Pages.Settings.WebRelatedAreaExpanded;
        set
        {
            ConfigService.AppConfig.Pages.Settings.WebRelatedAreaExpanded = value;

            ConfigService.SaveAll();
        }
    }

    internal static bool WebRelatedAreaOfNetworkInterfacesExpanded
    {
        get => ConfigService.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded;
        set
        {
            ConfigService.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded = value;

            ConfigService.SaveAll();
        }
    }

    internal static bool LogRelatedAreaExpanded
    {
        get => ConfigService.AppConfig.Pages.Settings.LogRelatedAreaExpanded;
        set
        {
            ConfigService.AppConfig.Pages.Settings.LogRelatedAreaExpanded = value;

            ConfigService.SaveAll();
        }
    }

    internal static bool UpdateRelatedAreaExpanded
    {
        get => ConfigService.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded;
        set
        {
            ConfigService.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded = value;

            ConfigService.SaveAll();
        }
    }

    internal static int LogFileSizeUsage => (int)(ConfigService.AppConfig.Log.LogFilePath.GetTotalSize() / 1000 / 1024);

    internal static int LogFileSizeLimit
    {
        get => (int)(ConfigService.AppConfig.Log.LogFileSingleMaxSize / 1024 / 1024);
        set
        {
            ConfigService.AppConfig.Log.LogFileSingleMaxSize = value * 1024 * 1024;

            var eventService = App.GetService<IEventService>();
            eventService.Publish(EventNames.LogConfigUpdated, EventArgs.Empty);

            ConfigService.SaveAll();
        }
    }

    internal static int LogFileMaxCount
    {
        get => ConfigService.AppConfig.Log.LogFileMaxCount;
        set
        {
            ConfigService.AppConfig.Log.LogFileMaxCount = value;

            var eventService = App.GetService<IEventService>();
            eventService.Publish(EventNames.LogConfigUpdated, EventArgs.Empty);

            ConfigService.SaveAll();
        }
    }

    internal static int LogFileFlushInterval
    {
        get => ConfigService.AppConfig.Log.LogFileFlushInterval;
        set
        {
            ConfigService.AppConfig.Log.LogFileFlushInterval = value;

            var eventService = App.GetService<IEventService>();
            eventService.Publish(EventNames.LogConfigUpdated, EventArgs.Empty);

            ConfigService.SaveAll();
        }
    }

    internal static int CheckerPerThreadFilesCountLimit
    {
        get => ConfigService.AppConfig.IO.UpdatingCheckPerThreadFilesCount;
        set
        {
            ConfigService.AppConfig.IO.UpdatingCheckPerThreadFilesCount = value;

            ConfigService.SaveAll();
        }
    }

    private static string GetLogLevelDisplayText(string key) => Translate(key, prefix: "Text_Log_") ?? string.Empty;

    internal static List<SupportedLogLevel> SupportedLogLevels { get; } =
        [
            new()
            {
                LogEventLevel = LogEventLevel.Verbose,
                LogLevelName = "Verbose",
                LogLevelDisplayName = GetLogLevelDisplayText("Verbose"),
            },
            new()
            {
                LogEventLevel = LogEventLevel.Debug,
                LogLevelName = "Debug",
                LogLevelDisplayName = GetLogLevelDisplayText("Debug"),
            },
            new()
            {
                LogEventLevel = LogEventLevel.Information,
                LogLevelName = "Information",
                LogLevelDisplayName = GetLogLevelDisplayText("Information"),
            },
            new()
            {
                LogEventLevel = LogEventLevel.Warning,
                LogLevelName = "Warning",
                LogLevelDisplayName = GetLogLevelDisplayText("Warning"),
            },
            new()
            {
                LogEventLevel = LogEventLevel.Error,
                LogLevelName = "Error",
                LogLevelDisplayName = GetLogLevelDisplayText("Error"),
            },
            new()
            {
                LogEventLevel = LogEventLevel.Fatal,
                LogLevelName = "Fatal",
                LogLevelDisplayName = GetLogLevelDisplayText("Fatal"),
            },
        ];

    private SupportedLogLevel? _currentLogLevel = SupportedLogLevels.Find(x => x.LogEventLevel == ConfigService.AppConfig.Log.LogLevel);

    internal SupportedLogLevel? CurrentLogLevel
    {
        get => _currentLogLevel;
        set
        {
            _currentLogLevel = value;

            if (value is not null)
            {
                ConfigService.AppConfig.Log.LogLevel = value.LogEventLevel;

                var eventService = App.GetService<IEventService>();
                eventService.Publish(EventNames.LogConfigUpdated, EventArgs.Empty);

                ConfigService.SaveAll();
            }
        }
    }

    internal ReactiveCommand<Unit, Unit>? EmptyLogsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RefreshLogsUsageCommand { get; set; }
}
