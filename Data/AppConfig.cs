﻿using Avalonia.Controls;
using Serilog.Events;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using FluentAvalonia.UI.Controls;

namespace KitX_Dashboard.Data;

/// <summary>
/// 配置结构
/// </summary>
public class AppConfig
{
    [JsonInclude]
    public Config_App App { get; set; } = new();

    [JsonInclude]
    public Config_Windows Windows { get; set; } = new();

    [JsonInclude]
    public Config_Pages Pages { get; set; } = new();

    [JsonInclude]
    public Config_Web Web { get; set; } = new();

    [JsonInclude]
    public Config_Log Log { get; set; } = new();

    [JsonInclude]
    public Config_IO IO { get; set; } = new();

    [JsonInclude]
    public Config_Activity Activity { get; set; } = new();

    [JsonInclude]
    public Config_Loaders Loaders { get; set; } = new();

    /// <summary>
    /// Config
    /// </summary>
    public class Config_App
    {
        [JsonInclude]
        public string IconFileName { get; set; } = "KitX-Icon-1920x-margin-2x.png";

        [JsonInclude]
        public string CoverIconFileName { get; set; } = "KitX-Icon-Background.png";

        [JsonInclude]
        public string AppLanguage { get; set; } = "zh-cn";

        [JsonInclude]
        public string Theme { get; set; } = "Follow";

        [JsonInclude]
        public string ThemeColor { get; set; } = "#FF3873D9";

        [JsonInclude]
        public Dictionary<string, string> SurpportLanguages { get; set; } = new()
        {
            { "zh-cn", "中文 (简体)" },
            { "zh-tw", "中文 (繁體)" },
            { "ru-ru", "Русский" },
            { "en-us", "English (US)" },
            { "fr-fr", "Français" },
            { "ja-jp", "日本語" },
            { "ko-kr", "한국어" },
        };

        [JsonInclude]
        public string LocalPluginsFileFolder { get; set; } = "./Plugins/";

        [JsonInclude]
        public string LocalPluginsDataFolder { get; set; } = "./PluginsDatas/";

        [JsonInclude]
        public bool DeveloperSetting { get; set; } = false;

        [JsonInclude]
        public bool ShowAnnouncementWhenStart { get; set; } = true;

        [JsonInclude]
        public ulong RanTime { get; set; } = 0;
    }

    /// <summary>
    /// WindowsConfig
    /// </summary>
    public class Config_Windows
    {

        [JsonInclude]
        public Config_MainWindow MainWindow { get; set; } = new();

        [JsonInclude]
        public Config_AnnouncementWindow AnnouncementWindow { get; set; } = new();

        /// <summary>
        /// MainWindowConfig
        /// </summary>
        public class Config_MainWindow
        {
            [JsonInclude]
            public double Window_Width { get; set; } = 1280;

            [JsonInclude]
            public double Window_Height { get; set; } = 720;

            [JsonInclude]
            public int Window_Left { get; set; } = -1;

            [JsonInclude]
            public int Window_Top { get; set; } = -1;

            [JsonInclude]
            public WindowState WindowState { get; set; } = WindowState.Normal;

            [JsonInclude]
            public bool IsHidden { get; set; } = false;

            [JsonInclude]
            public Dictionary<string, string> Tags { get; set; } = new()
            {
                { "SelectedPage", "Page_Home" }
            };

            [JsonInclude]
            public bool EnabledMica { get; set; } = true;

            [JsonInclude]
            public double MicaOpacity { get; set; } = 0.15;

            [JsonInclude]
            public int GreetingTextCount_Morning { get; set; } = 5;

            [JsonInclude]
            public int GreetingTextCount_Noon { get; set; } = 3;

            [JsonInclude]
            public int GreetingTextCount_AfterNoon { get; set; } = 3;

            [JsonInclude]
            public int GreetingTextCount_Evening { get; set; } = 2;

            [JsonInclude]
            public int GreetingTextCount_Night { get; set; } = 4;

            [JsonInclude]
            public int GreetingUpdateInterval { get; set; } = 10;
        }

        /// <summary>
        /// AnnouncementWindowConfig
        /// </summary>
        public class Config_AnnouncementWindow
        {
            [JsonInclude]
            public double Window_Width { get; set; } = 1280;

            [JsonInclude]
            public double Window_Height { get; set; } = 720;

            [JsonInclude]
            public int Window_Left { get; set; } = -1;

            [JsonInclude]
            public int Window_Top { get; set; } = -1;
        }
    }

    /// <summary>
    /// ViewsConfig
    /// </summary>
    public class Config_Pages
    {
        [JsonInclude]
        public Config_HomePage Home { get; set; } = new();

        [JsonInclude]
        public Config_DevicePage Device { get; set; } = new();

        [JsonInclude]
        public Config_MarketPage Market { get; set; } = new();

        [JsonInclude]
        public Config_SettingsPage Settings { get; set; } = new();

        /// <summary>
        /// HomePageConfig
        /// </summary>
        public class Config_HomePage
        {
            [JsonInclude]
            public NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; }
                = NavigationViewPaneDisplayMode.Auto;

            [JsonInclude]
            public string SelectedViewName { get; set; } = "View_Recent";

            [JsonInclude]
            public bool IsNavigationViewPaneOpened { get; set; } = true;

            [JsonInclude]
            public bool UseAreaExpanded { get; set; } = true;
        }

        /// <summary>
        /// DevicePageConfig
        /// </summary>
        public class Config_DevicePage
        {

        }

        /// <summary>
        /// MargetPageConfig
        /// </summary>
        public class Config_MarketPage
        {

        }

        /// <summary>
        /// SettingsPageConfig
        /// </summary>
        public class Config_SettingsPage
        {
            [JsonInclude]
            public NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; }
                = NavigationViewPaneDisplayMode.Auto;

            [JsonInclude]
            public string SelectedViewName { get; set; } = "View_General";

            [JsonInclude]
            public bool MicaAreaExpanded { get; set; } = true;

            [JsonInclude]
            public bool MicaToolTipIsOpen { get; set; } = true;

            [JsonInclude]
            public bool PaletteAreaExpanded { get; set; } = false;

            [JsonInclude]
            public bool WebRelatedAreaExpanded { get; set; } = true;

            [JsonInclude]
            public bool WebRelatedAreaOfNetworkInterfacesExpanded { get; set; } = false;

            [JsonInclude]
            public bool LogRelatedAreaExpanded { get; set; } = true;

            [JsonInclude]
            public bool UpdateRelatedAreaExpanded { get; set; } = true;

            [JsonInclude]
            public bool AboutAreaExpanded { get; set; } = false;

            [JsonInclude]
            public bool AuthorsAreaExpanded { get; set; } = false;

            [JsonInclude]
            public bool LinksAreaExpanded { get; set; } = false;

            [JsonInclude]
            public bool ThirdPartyLicensesAreaExpanded { get; set; } = false;

            [JsonInclude]
            public bool IsNavigationViewPaneOpened { get; set; } = true;
        }
    }

    /// <summary>
    /// WebConfig
    /// </summary>
    public class Config_Web
    {
        [JsonInclude]
        public int DelayStartSeconds { get; set; } = 3;

        [JsonInclude]
        public string APIServer { get; set; } = "api.catrol.cn";

        [JsonInclude]
        public string APIPath { get; set; } = "/apps/kitx/";

        [JsonInclude]
        public int DevicesViewRefreshDelay { get; set; } = 1000;

        [JsonInclude]
        public List<string>? AcceptedNetworkInterfaces { get; set; } = null;

        [JsonInclude]
        public int? UserSpecifiedPluginsServerPort { get; set; } = null;

        [JsonInclude]
        public int UDPPortSend { get; set; } = 23404;

        [JsonInclude]
        public int UDPPortReceive { get; set; } = 24040;

        [JsonInclude]
        public int UDPSendFrequency { get; set; } = 2000;

        [JsonInclude]
        public string UDPBroadcastAddress { get; set; } = "224.0.0.0";

        [JsonInclude]
        public string IPFilter { get; set; } = "192.168";

        [JsonInclude]
        public int SocketBufferSize { get; set; } = 1024 * 100;

        [JsonInclude]
        public int DeviceInfoStructTTLSeconds { get; set; } = 7;

        [JsonInclude]
        public string UpdateServer { get; set; } = "api.catrol.cn";

        /// <summary>
        /// %platform%  -   Windows, Linux, MacOS (win, linux, mac)
        /// </summary>
        [JsonInclude]
        public string UpdatePath { get; set; } = "/apps/kitx/%platform%/";

        [JsonInclude]
        public string UpdateDownloadPath { get; set; } = "/apps/kitx/update/%platform%/";

        /// <summary>
        /// %channel%   -   Stable, Beta, Alpha (stable, beta, alpha)
        /// </summary>
        [JsonInclude]
        public string UpdateChannel { get; set; } = "stable";

        [JsonInclude]
        public string UpdateSource { get; set; } = "latest-components.json";
    }

    /// <summary>
    /// LogConfig
    /// </summary>
    public class Config_Log
    {

        [JsonInclude]
        public long LogFileSingleMaxSize { get; set; } = 1024 * 1024 * 10;      //  10MB

        [JsonInclude]
        public string LogFilePath { get; set; } = "./Log/";

        [JsonInclude]
        public string LogTemplate { get; set; } = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] " +
            "[{Level:u3}] {Message:lj}{NewLine}{Exception}";

        [JsonInclude]
        public int LogFileMaxCount { get; set; } = 50;

        [JsonInclude]
        public int LogFileFlushInterval { get; set; } = 30;

#if DEBUG

        [JsonInclude]
        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;

#else

        [JsonInclude]
        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Warning;
        
#endif

    }

    /// <summary>
    /// IOConfig
    /// </summary>
    public class Config_IO
    {
        [JsonInclude]
        public int UpdatingCheckPerThreadFilesCount { get; set; } = 20;
    }

    /// <summary>
    /// ActivityConfig
    /// </summary>
    public class Config_Activity
    {
        [JsonInclude]
        public int TotalRecorded { get; set; } = 0;
    }

    /// <summary>
    /// LoadersConfig
    /// </summary>
    public class Config_Loaders
    {
        [JsonInclude]
        public string InstallPath { get; set; } = "./Loaders/";
    }
}

//
//                   _______________________________________________________
//                   |                                                      |
//              /    |                                                      |
//             /---, |                                                      |
//        -----# ==| |                                                      |
//        | :) # ==| |                                                      |
//   -----'----#   | |______________________________________________________|
//   |)___()  '#   |______====____   \___________________________________|
//  [_/,-,\"--"------ //,-,  ,-,\\\   |/             //,-,  ,-,  ,-,\\ __#
//    ( 0 )|===******||( 0 )( 0 )||-  o              '( 0 )( 0 )( 0 )||
// ----'-'--------------'-'--'-'-----------------------'-'--'-'--'-'--------------
//
