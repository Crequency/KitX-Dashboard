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
using Common.BasicHelper.Core.TaskSystem;
using Common.BasicHelper.Utils.Extensions;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Tasks;
using KitX.Core.Tasks;
using KitX.Dashboard;
using KitX.Dashboard.Models;
using KitX.Dashboard.Names;
using KitX.Dashboard.Services;
using ReactiveUI;
using Serilog;
using Serilog.Events;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_PerformanceViewModel : ViewModelBase, IDisposable
{
    private readonly ITasksService _tasksService;
    private readonly IConfigService _configService;
    private readonly IEventService _eventService;
    private readonly SignalTasksManager _signalTasksManager;

    /// <summary>Named handlers so <see cref="Dispose"/> can unsubscribe them (D11).</summary>
    private readonly EventHandler<EventArgs> _logConfigUpdatedHandler;

    private readonly EventHandler<EventArgs> _languageChangedHandler;

    private readonly EventHandler<PortChangedEventArgs> _devicesServerPortChangedHandler;

    private readonly EventHandler<PortChangedEventArgs> _pluginsServerPortChangedHandler;

    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _selectedNetworkInterfacesChangedHandler;

    public Settings_PerformanceViewModel(
        ITasksService tasksService,
        IConfigService configService,
        IEventService eventService,
        SignalTasksManager signalTasksManager)
    {
        _tasksService = tasksService;
        _configService = configService;
        _eventService = eventService;
        _signalTasksManager = signalTasksManager;

        _logConfigUpdatedHandler = (s, e) => LoggerConfigurator.Configure(_configService.AppConfig.Log);

        _languageChangedHandler = (s, e) =>
        {
            foreach (var item in SupportedLogLevels)
                item.LogLevelDisplayName = GetLogLevelDisplayText(item.LogLevelName ?? "");

            this.RaisePropertyChanged(nameof(SupportedLogLevels));
        };

        _devicesServerPortChangedHandler = (s, e) => this.RaisePropertyChanged(nameof(DevicesServerPort));

        _pluginsServerPortChangedHandler = (s, e) => this.RaisePropertyChanged(nameof(PluginsServerPort));

        _selectedNetworkInterfacesChangedHandler = (_, _) =>
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

            _configService.SaveAll();
        };

        _currentLogLevel = SupportedLogLevels.Find(x => x.LogEventLevel == (LogEventLevel)_configService.AppConfig.Log.LogLevel);

        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        EmptyLogsCommand = ReactiveCommand.Create(() =>
        {
            const string location = $"{nameof(Settings_PerformanceViewModel)}.{nameof(EmptyLogsCommand)}";

            _tasksService.RunTask(() =>
            {
                var dir = new DirectoryInfo(_configService.AppConfig.Log.LogFilePath.GetFullPath());

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

                RefreshLogFileSizeUsage();

                this.RaisePropertyChanged(nameof(LogFileSizeUsage));
            }, nameof(EmptyLogsCommand));
        });

        RefreshLogsUsageCommand = ReactiveCommand.Create(() =>
        {
            RefreshLogFileSizeUsage();

            this.RaisePropertyChanged(nameof(LogFileSizeUsage));
        });
    }

    public sealed override void InitEvents()
    {
        _eventService.Subscribe(EventNames.LogConfigUpdated, _logConfigUpdatedHandler);

        _eventService.Subscribe(EventNames.LanguageChanged, _languageChangedHandler);

        _eventService.Subscribe<PortChangedEventArgs>(EventNames.DevicesServerPortChanged, _devicesServerPortChangedHandler);

        _eventService.Subscribe<PortChangedEventArgs>(EventNames.PluginsServerPortChanged, _pluginsServerPortChangedHandler);

        _signalTasksManager.SignalRun(
            nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal),
            () =>
            {
                RefreshAvailableNetworkInterfaces();

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
            SelectedNetworkInterfaces.CollectionChanged += _selectedNetworkInterfacesChangedHandler;
    }

    /// <summary>
    /// Unsubscribes every subscription made in <see cref="InitEvents"/>. The VM is
    /// DI-transient and recreated on each Settings navigation (D11).
    /// </summary>
    public void Dispose()
    {
        _eventService.Unsubscribe(EventNames.LogConfigUpdated, _logConfigUpdatedHandler);
        _eventService.Unsubscribe(EventNames.LanguageChanged, _languageChangedHandler);
        _eventService.Unsubscribe(EventNames.DevicesServerPortChanged, _devicesServerPortChangedHandler);
        _eventService.Unsubscribe(EventNames.PluginsServerPortChanged, _pluginsServerPortChangedHandler);

        if (SelectedNetworkInterfaces is not null)
            SelectedNetworkInterfaces.CollectionChanged -= _selectedNetworkInterfacesChangedHandler;
    }

    internal double DelayedWebStartSeconds
    {
        get => _configService.AppConfig.Web.DelayStartSeconds;
        set
        {
            _configService.AppConfig.Web.DelayStartSeconds = value;
            _configService.SaveAll();
        }
    }

    internal bool PluginsServerPortEditable => PluginsServerPortType != 0;

    internal int PluginsServerPortType
    {
        get => _configService.AppConfig.Web.UserSpecifiedPluginsServerPort is null ? 0 : 1;
        set
        {
            if (value == 0)
                _configService.AppConfig.Web.UserSpecifiedPluginsServerPort = null;
            else
                _configService.AppConfig.Web.UserSpecifiedPluginsServerPort = PluginsServerPort;

            this.RaisePropertyChanged(nameof(PluginsServerPortEditable));

            _configService.SaveAll();
        }
    }

    internal int PluginsServerPort
    {
        get => ConstantTable.PluginsServerPort;
        set
        {
            if (value >= 0 && value <= 65535)
                _configService.AppConfig.Web.UserSpecifiedPluginsServerPort = value;
        }
    }

    internal int DevicesServerPort => ConstantTable.DevicesServerPort;

    internal string LocalIPFilter
    {
        get => _configService.AppConfig.Web.IPFilter;
        set
        {
            _configService.AppConfig.Web.IPFilter = value;

            _configService.SaveAll();
        }
    }

    internal string AcceptedNetworkInterfacesNames
    {
        get
        {
            var userPointed = _configService.AppConfig.Web.AcceptedNetworkInterfaces;

            if (userPointed is null)
                return "Auto";
            else
                return userPointed.ToCustomString(";");
        }
        set
        {
            if (value.ToLower().Equals("auto"))
                _configService.AppConfig.Web.AcceptedNetworkInterfaces = null;
            else
            {
                var userInput = value.Split(';');

                _configService.AppConfig.Web.AcceptedNetworkInterfaces = [.. userInput];
            }
        }
    }

    private ObservableCollection<string>? _availableNetworkInterfaces;

    internal ObservableCollection<string>? AvailableNetworkInterfaces
    {
        get
        {
            if (_availableNetworkInterfaces is null)
                RefreshAvailableNetworkInterfaces();

            return _availableNetworkInterfaces;
        }
    }

    private void RefreshAvailableNetworkInterfaces()
    {
        _availableNetworkInterfaces = new(
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                               nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                .Select(x => x.Name)
        );
    }

    internal ObservableCollection<string>? SelectedNetworkInterfaces { get; } = [];

    internal int DevicesListRefreshDelay
    {
        get => _configService.AppConfig.Web.DevicesViewRefreshDelay;
        set
        {
            _configService.AppConfig.Web.DevicesViewRefreshDelay = value;

            _configService.SaveAll();
        }
    }

    internal int GreetingTextUpdateInterval
    {
        get => _configService.AppConfig.Windows.MainWindow.GreetingUpdateInterval;
        set
        {
            _configService.AppConfig.Windows.MainWindow.GreetingUpdateInterval = value;

            _eventService.Publish(EventNames.GreetingTextIntervalUpdated, EventArgs.Empty);

            _configService.SaveAll();
        }
    }

    internal bool WebRelatedAreaExpanded
    {
        get => _configService.AppConfig.Pages.Settings.WebRelatedAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.WebRelatedAreaExpanded = value;

            _configService.SaveAll();
        }
    }

    internal bool WebRelatedAreaOfNetworkInterfacesExpanded
    {
        get => _configService.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded = value;

            _configService.SaveAll();
        }
    }

    internal bool LogRelatedAreaExpanded
    {
        get => _configService.AppConfig.Pages.Settings.LogRelatedAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.LogRelatedAreaExpanded = value;

            _configService.SaveAll();
        }
    }

    internal bool UpdateRelatedAreaExpanded
    {
        get => _configService.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded = value;

            _configService.SaveAll();
        }
    }

    private int _logFileSizeUsageCache;

    private bool _logFileSizeUsageCacheValid;

    internal int LogFileSizeUsage
    {
        get
        {
            if (!_logFileSizeUsageCacheValid)
            {
                _logFileSizeUsageCache = (int)(_configService.AppConfig.Log.LogFilePath.GetTotalSize() / 1000 / 1024);
                _logFileSizeUsageCacheValid = true;
            }

            return _logFileSizeUsageCache;
        }
    }

    private void RefreshLogFileSizeUsage()
    {
        _logFileSizeUsageCacheValid = false;

        _ = LogFileSizeUsage;
    }

    internal int LogFileSizeLimit
    {
        get => (int)(_configService.AppConfig.Log.LogFileSingleMaxSize / 1024 / 1024);
        set
        {
            _configService.AppConfig.Log.LogFileSingleMaxSize = value * 1024 * 1024;

            _eventService.Publish(EventNames.LogConfigUpdated, EventArgs.Empty);

            _configService.SaveAll();
        }
    }

    internal int LogFileMaxCount
    {
        get => _configService.AppConfig.Log.LogFileMaxCount;
        set
        {
            _configService.AppConfig.Log.LogFileMaxCount = value;

            _eventService.Publish(EventNames.LogConfigUpdated, EventArgs.Empty);

            _configService.SaveAll();
        }
    }

    internal int LogFileFlushInterval
    {
        get => _configService.AppConfig.Log.LogFileFlushInterval;
        set
        {
            _configService.AppConfig.Log.LogFileFlushInterval = value;

            _eventService.Publish(EventNames.LogConfigUpdated, EventArgs.Empty);

            _configService.SaveAll();
        }
    }

    internal int CheckerPerThreadFilesCountLimit
    {
        get => _configService.AppConfig.IO.UpdatingCheckPerThreadFilesCount;
        set
        {
            _configService.AppConfig.IO.UpdatingCheckPerThreadFilesCount = value;

            _configService.SaveAll();
        }
    }

    private static string GetLogLevelDisplayText(string key) => Translate(key, prefix: "Text_Log_") ?? string.Empty;

    internal List<SupportedLogLevel> SupportedLogLevels { get; } =
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

    private SupportedLogLevel? _currentLogLevel;

    internal SupportedLogLevel? CurrentLogLevel
    {
        get => _currentLogLevel;
        set
        {
            _currentLogLevel = value;

            if (value is not null)
            {
                _configService.AppConfig.Log.LogLevel = (KitX.Core.Contract.Configuration.LogLevel)value.LogEventLevel;

                _eventService.Publish(EventNames.LogConfigUpdated, EventArgs.Empty);

                _configService.SaveAll();
            }
        }
    }

    internal ReactiveCommand<Unit, Unit>? EmptyLogsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RefreshLogsUsageCommand { get; set; }
}
