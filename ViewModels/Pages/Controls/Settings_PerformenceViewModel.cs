using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Models;
using KitX_Dashboard.Names;
using KitX_Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class Settings_PerformenceViewModel : ViewModelBase, INotifyPropertyChanged
{

    internal Settings_PerformenceViewModel()
    {
        InitEvents();

        InitCommands();
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
                    flushToDiskInterval: new(0, 0, ConfigManager.AppConfig.Log.LogFileFlushInterval),
                    restrictedToMinimumLevel: ConfigManager.AppConfig.Log.LogLevel,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: ConfigManager.AppConfig.Log.LogFileMaxCount
                )
                .CreateLogger();
        };

        EventService.LanguageChanged += () =>
        {
            foreach (var item in SupportedLogLevels)
                item.LogLevelDisplayName = GetLogLevelInLanguages(item.LogLevelName);
            PropertyChanged?.Invoke(this, new(nameof(SupportedLogLevels)));
        };

        EventService.DevicesServerPortChanged += () => PropertyChanged?.Invoke(this,
                new(nameof(DevicesServerPort)));

        EventService.PluginsServerPortChanged += () => PropertyChanged?.Invoke(this,
                new(nameof(PluginsServerPort)));

        Program.TasksManager?.SignalRun(
            nameof(SignalsNames.FinishedFindingNetworkInterfacesSignal),
            () =>
            {
                PropertyChanged?.Invoke(this,
                    new(nameof(AvailableNetworkInterfaces)));

                Dispatcher.UIThread.Post(() =>
                {
                    var anin = AcceptedNetworkInterfacesNames;
                    if (anin is not null && !anin.Equals("Auto"))
                        foreach (var item in anin.Split(';'))
                            if (AvailableNetworkInterfaces is not null &&
                                AvailableNetworkInterfaces.Contains(item))
                                SelectedNetworkInterfaces?.Add(item);
                });
            }
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
                PropertyChanged?.Invoke(this,
                    new(nameof(AcceptedNetworkInterfacesNames)));

                SaveChanges();
            };
    }

    private void InitCommands()
    {
        EmptyLogsCommand = new(EmptyLogs);
        RefreshLogsUsageCommand = new(RefreshLogsUsage);
    }

    /// <summary>
    /// 保存变更
    /// </summary>
    private static void SaveChanges()
    {
        EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
    }

    /// <summary>
    /// 网络服务启动延时
    /// </summary>
    internal static int DelayedWebStartSeconds
    {
        get => ConfigManager.AppConfig.Web.DelayStartSeconds;
        set
        {
            ConfigManager.AppConfig.Web.DelayStartSeconds = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 插件服务器端口是否可以编辑
    /// </summary>
    internal bool PluginsServerPortEditable => PluginsServerPortType != 0;

    /// <summary>
    /// 插件服务器端口类型
    /// </summary>
    internal int PluginsServerPortType
    {
        get => ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort is null ? 0 : 1;
        set
        {
            if (value == 0)
                ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort = null;
            else
                ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort = PluginsServerPort;
            PropertyChanged?.Invoke(this,
                new(nameof(PluginsServerPortEditable)));
            SaveChanges();
        }
    }

    /// <summary>
    /// 插件间服务端口属性
    /// </summary>
    internal static int PluginsServerPort
    {
        get => GlobalInfo.PluginServerPort;
        set
        {
            if (value >= 0 && value <= 65535)
                ConfigManager.AppConfig.Web.UserSpecifiedPluginsServerPort = value;
        }
    }

    /// <summary>
    /// 设备间服务端口属性
    /// </summary>
    internal static int DevicesServerPort => GlobalInfo.DeviceServerPort;

    /// <summary>
    /// 本机IP地址过滤规则
    /// </summary>
    internal static string LocalIPFilter
    {
        //get
        //{
        //    var value = Program.AppConfig.Web.IPFilter;
        //    var parts = value.Split('.').ToList();
        //    var ints = new List<int>();
        //    parts.ForEach(x => ints.Add(int.Parse(x)));
        //    var sb = new StringBuilder();
        //    for (var i = 0; i < ints.Count; ++i)
        //    {
        //        var part = ints[i];
        //        sb.Append(part.ToString().PadLeft(3, '0'));
        //        if (i != 3) sb.Append('.');
        //    }
        //    var delta = 4 - ints.Count;
        //    while (delta > 0)
        //    {
        //        sb.Append(".___");
        //        --delta;
        //    }
        //    return sb.ToString();
        //}
        //set
        //{
        //    var parts = value.Split('.').ToList();
        //    var sb = new StringBuilder();
        //    parts.ForEach(p =>
        //    {
        //        sb.Append(int.Parse(p.Replace("_", "")));
        //        sb.Append('.');
        //    });
        //    Program.AppConfig.Web.IPFilter = sb.ToString()[..^1];
        //    SaveChanges();
        //}
        get => ConfigManager.AppConfig.Web.IPFilter;
        set
        {
            ConfigManager.AppConfig.Web.IPFilter = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 指定的网络适配器
    /// </summary>
    internal static string AcceptedNetworkInterfacesNames
    {
        get
        {
            var userPointed = ConfigManager.AppConfig.Web.AcceptedNetworkInterfaces;
            if (userPointed is null) return "Auto";
            else return userPointed.ToCustomString(";")[..^1];
        }
        set
        {
            if (value.Equals("Auto"))
                ConfigManager.AppConfig.Web.AcceptedNetworkInterfaces = null;
            else
            {
                var userInput = value.Split(';');
                ConfigManager.AppConfig.Web.AcceptedNetworkInterfaces = userInput.ToList();
            }
        }
    }

    /// <summary>
    /// 可用的网络适配器
    /// </summary>
    internal static ObservableCollection<string>? AvailableNetworkInterfaces
        => Program.WebManager?.NetworkInterfaceRegistered;

    /// <summary>
    /// 用户选择的网络适配器
    /// </summary>
    internal static ObservableCollection<string>? SelectedNetworkInterfaces { get; }
        = new();

    /// <summary>
    /// 设备列表刷新延迟
    /// </summary>
    internal static int DevicesListRefreshDelay
    {
        get => ConfigManager.AppConfig.Web.DevicesViewRefreshDelay;
        set
        {
            ConfigManager.AppConfig.Web.DevicesViewRefreshDelay = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 招呼语更新延迟
    /// </summary>
    internal static int GreetingTextUpdateInterval
    {
        get => ConfigManager.AppConfig.Windows.MainWindow.GreetingUpdateInterval;
        set
        {
            ConfigManager.AppConfig.Windows.MainWindow.GreetingUpdateInterval = value;
            EventService.Invoke(nameof(EventService.GreetingTextIntervalUpdated));
            SaveChanges();
        }
    }

    /// <summary>
    /// 网络相关设置区域是否展开
    /// </summary>
    internal static bool WebRelatedAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.WebRelatedAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.WebRelatedAreaExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 网络相关设置中网络适配器区域是否展开
    /// </summary>
    internal static bool WebRelatedAreaOfNetworkInterfacesExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.WebRelatedAreaOfNetworkInterfacesExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 日志相关设置区域是否展开
    /// </summary>
    internal static bool LogRelatedAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.LogRelatedAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.LogRelatedAreaExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 更新相关设置区域是否展开
    /// </summary>
    internal static bool UpdateRelatedAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.UpdateRelatedAreaExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 日志文件空间占用
    /// 单位, MB
    /// </summary>
    internal static int LogFileSizeUsage
        => (int)(ConfigManager.AppConfig.Log.LogFilePath.GetTotalSize() / 1000 / 1024);

    /// <summary>
    /// 单个文件体积限制上限
    /// </summary>
    internal static int LogFileSizeLimit
    {
        get => (int)(ConfigManager.AppConfig.Log.LogFileSingleMaxSize / 1024 / 1024);
        set
        {
            ConfigManager.AppConfig.Log.LogFileSingleMaxSize = value * 1024 * 1024;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveChanges();
        }
    }

    /// <summary>
    /// 日志文件数量限制
    /// </summary>
    internal static int LogFileMaxCount
    {
        get => ConfigManager.AppConfig.Log.LogFileMaxCount;
        set
        {
            ConfigManager.AppConfig.Log.LogFileMaxCount = value;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveChanges();
        }
    }

    /// <summary>
    /// 日志文件缓冲延迟
    /// </summary>
    internal static int LogFileFlushInterval
    {
        get => ConfigManager.AppConfig.Log.LogFileFlushInterval;
        set
        {
            ConfigManager.AppConfig.Log.LogFileFlushInterval = value;
            EventService.Invoke(nameof(EventService.LogConfigUpdated));
            SaveChanges();
        }
    }

    /// <summary>
    /// 更新器每个线程检查文件数量限制
    /// </summary>
    internal static int CheckerPerThreadFilesCountLimit
    {
        get => ConfigManager.AppConfig.IO.UpdatingCheckPerThreadFilesCount;
        set
        {
            ConfigManager.AppConfig.IO.UpdatingCheckPerThreadFilesCount = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 获取日志级别的显示名称
    /// </summary>
    /// <param name="key">日志级别</param>
    /// <returns>显示名称</returns>
    internal static string GetLogLevelInLanguages(string key)
    {
        if (Application.Current.TryFindResource($"Text_Log_{key}",
            out object? result))
            if (result != null) return (string)result;
            else return string.Empty;
        else return string.Empty;
    }

    /// <summary>
    /// 支持的日志级别列表
    /// </summary>
    internal static List<SupportedLogLevel> SupportedLogLevels { get; } = new()
    {
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Verbose,
            LogLevelName = "Verbose",
            LogLevelDisplayName = GetLogLevelInLanguages("Verbose")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Debug,
            LogLevelName = "Debug",
            LogLevelDisplayName = GetLogLevelInLanguages("Debug")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Information,
            LogLevelName = "Information",
            LogLevelDisplayName = GetLogLevelInLanguages("Information")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Warning,
            LogLevelName = "Warning",
            LogLevelDisplayName = GetLogLevelInLanguages("Warning")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Error,
            LogLevelName = "Error",
            LogLevelDisplayName = GetLogLevelInLanguages("Error")
        },
        new()
        {
            LogEventLevel = Serilog.Events.LogEventLevel.Fatal,
            LogLevelName = "Fatal",
            LogLevelDisplayName = GetLogLevelInLanguages("Fatal")
        },
    };

    internal SupportedLogLevel? _currentLogLevel = SupportedLogLevels.Find(
        x => x.LogEventLevel == ConfigManager.AppConfig.Log.LogLevel);

    /// <summary>
    /// 当前日志记录级别
    /// </summary>
    internal SupportedLogLevel? CurrentLogLevel
    {
        get => _currentLogLevel;
        set
        {
            _currentLogLevel = value;
            if (value != null)
            {
                ConfigManager.AppConfig.Log.LogLevel = value.LogEventLevel;
                EventService.Invoke(nameof(EventService.LogConfigUpdated));
                SaveChanges();
            }
        }
    }

    /// <summary>
    /// 清空日志命令
    /// </summary>
    internal DelegateCommand? EmptyLogsCommand { get; set; }

    /// <summary>
    /// 刷新日志占用命令
    /// </summary>
    internal DelegateCommand? RefreshLogsUsageCommand { get; set; }

    private void EmptyLogs(object obj)
    {
        var location = $"{nameof(Settings_PerformenceViewModel)}.{nameof(EmptyLogs)}";

        Task.Run(() =>
        {
            var dir = new DirectoryInfo(ConfigManager.AppConfig.Log.LogFilePath.GetFullPath());

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
                new(nameof(LogFileSizeUsage)));
        });
    }

    private void RefreshLogsUsage(object obj) =>
        PropertyChanged?.Invoke(this, new(nameof(LogFileSizeUsage)));

    public new event PropertyChangedEventHandler? PropertyChanged;
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。

//                     ______
//                 -~~`      `~~~~---,__
//                                      `~-.
//               __,--~~~~~~---,__          `\
//           _/~~                 `~-,_       `\
//        _/~                          `\       `.
//      /'          _,--~~~~~--,_        `\      `\
//    /'         /~~             ~\        |       |
//   /'        /'     __,---,_     `\      `|      `|
//  .'       ,'     /~        ~~\    `.     |       |
//  |        |     |      /~~\   |    |     `|      |
//  |        |     |     |   '   |    |      |      |
//  |        |     |     `\.__,-'    .'      |      |
//  `|        \     `\_             /       .'     .'
//   `|        `\      `--,_____,--'       /       |
//     \         `\                      /'       /
//      `\         `-,__            _,--'      _/'
//        `\_           ~~~------~~~       _,-~
//           ~~--_                   ___,-~
//                `~~~~~------'~~~~~'
