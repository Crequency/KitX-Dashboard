﻿using Avalonia;
using Avalonia.Controls;
using Common.BasicHelper.IO;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Models;
using KitX_Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Settings_PerformenceViewModel : ViewModelBase, INotifyPropertyChanged
    {

        internal Settings_PerformenceViewModel()
        {
            InitEvents();

            InitCommands();
        }

        private void InitEvents()
        {
            EventHandlers.LogConfigUpdated += () =>
            {
                string logdir = Path.GetFullPath(Program.Config.Log.LogFilePath);
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
            };
            EventHandlers.LanguageChanged += () =>
            {
                foreach (var item in SurpportLogLevels)
                    item.LogLevelDisplayName = GetLogLevelInLanguages(item.LogLevelName);
                PropertyChanged?.Invoke(this, new(nameof(SurpportLogLevels)));
            };
            EventHandlers.DevicesServerPortChanged += () =>
            {
                PropertyChanged?.Invoke(this, new(nameof(DevicesServerPort)));
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
            EventHandlers.Invoke(nameof(EventHandlers.ConfigSettingsChanged));
        }

        /// <summary>
        /// 插件间服务端口属性
        /// </summary>
        internal static int PluginsServerPort => GlobalInfo.PluginServerPort;

        /// <summary>
        /// 设备间服务端口属性
        /// </summary>
        internal static int DevicesServerPort => GlobalInfo.DeviceServerPort;

        /// <summary>
        /// 本机IP地址过滤规则
        /// </summary>
        internal static string LocalIPFilter
        {
            get => Program.Config.Web.IPFilter;
            set
            {
                Program.Config.Web.IPFilter = value;
                SaveChanges();
            }
        }

        /// <summary>
        /// 招呼语更新延迟
        /// </summary>
        internal static int GreetingTextUpdateInterval
        {
            get => Program.Config.Windows.MainWindow.GreetingUpdateInterval;
            set
            {
                Program.Config.Windows.MainWindow.GreetingUpdateInterval = value;
                EventHandlers.Invoke(nameof(EventHandlers.GreetingTextIntervalUpdated));
                SaveChanges();
            }
        }

        /// <summary>
        /// 日志相关设置区域是否展开
        /// </summary>
        internal static bool LogReletiveAreaExpanded
        {
            get => Program.Config.Pages.Settings.LogReletiveAreaExpanded;
            set
            {
                Program.Config.Pages.Settings.LogReletiveAreaExpanded = value;
                SaveChanges();
            }
        }

        /// <summary>
        /// 日志文件空间占用
        /// 单位, MB
        /// </summary>
        internal static int LogFileSizeUsage => (int)(DirectoryHelper.GetDirectorySize
            (Path.GetFullPath(Program.Config.Log.LogFilePath)) / 1000 / 1024);

        /// <summary>
        /// 单个文件体积限制上限
        /// </summary>
        internal static int LogFileSizeLimit
        {
            get => (int)(Program.Config.Log.LogFileSingleMaxSize / 1024 / 1024);
            set
            {
                Program.Config.Log.LogFileSingleMaxSize = value * 1024 * 1024;
                EventHandlers.Invoke(nameof(EventHandlers.LogConfigUpdated));
                SaveChanges();
            }
        }

        /// <summary>
        /// 日志文件数量限制
        /// </summary>
        internal static int LogFileMaxCount
        {
            get => Program.Config.Log.LogFileMaxCount;
            set
            {
                Program.Config.Log.LogFileMaxCount = value;
                EventHandlers.Invoke(nameof(EventHandlers.LogConfigUpdated));
                SaveChanges();
            }
        }

        /// <summary>
        /// 日志文件缓冲延迟
        /// </summary>
        internal static int LogFileFlushInterval
        {
            get => Program.Config.Log.LogFileFlushInterval;
            set
            {
                Program.Config.Log.LogFileFlushInterval = value;
                EventHandlers.Invoke(nameof(EventHandlers.LogConfigUpdated));
                SaveChanges();
            }
        }

        /// <summary>
        /// 更新器每个线程检查文件数量限制
        /// </summary>
        internal static int CheckerPerThreadFilesCountLimit
        {
            get => Program.Config.IO.UpdatingCheckPerThreadFilesCount;
            set
            {
                Program.Config.IO.UpdatingCheckPerThreadFilesCount = value;
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
        internal static List<SurpportLogLevel> SurpportLogLevels { get; } = new()
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

        internal SurpportLogLevel? _currentLogLevel = SurpportLogLevels.Find(
            x => x.LogEventLevel == Program.Config.Log.LogLevel);

        /// <summary>
        /// 当前日志记录级别
        /// </summary>
        internal SurpportLogLevel? CurrentLogLevel
        {
            get => _currentLogLevel;
            set
            {
                _currentLogLevel = value;
                if (value != null)
                {
                    Program.Config.Log.LogLevel = value.LogEventLevel;
                    EventHandlers.Invoke(nameof(EventHandlers.LogConfigUpdated));
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
            try
            {
                new Thread(() =>
                {
                    DirectoryInfo dir = new(Path.GetFullPath(Program.Config.Log.LogFilePath));
                    foreach (var file in dir.GetFiles())
                    {
                        try
                        {
                            File.Delete(file.FullName);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"In Settings_Performence.EmptyLogs(): {ex.Message}");
                        }
                    }
                    PropertyChanged?.Invoke(this, new(nameof(LogFileSizeUsage)));
                }).Start();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In Settings_Performence.EmptyLogs(): {ex.Message}");
            }
        }

        private void RefreshLogsUsage(object obj) =>
            PropertyChanged?.Invoke(this, new(nameof(LogFileSizeUsage)));

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
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
