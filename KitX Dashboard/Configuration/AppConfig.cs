using System.Collections.Generic;
using Avalonia.Controls;
using Common.BasicHelper.Graphics.Screen;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Configuration.Interfaces;
using Serilog.Events;

namespace KitX.Dashboard.Configuration;

public class AppConfig : ConfigBase
{
    public Config_App App { get; set; } = new();

    public Config_Windows Windows { get; set; } = new();

    public Config_Pages Pages { get; set; } = new();

    public Config_Web Web { get; set; } = new();

    public Config_Log Log { get; set; } = new();

    public Config_IO IO { get; set; } = new();

    public Config_Activity Activity { get; set; } = new();

    public Config_Loaders Loaders { get; set; } = new();

    public class Config_App
    {
        public string IconFileName { get; set; } = "KitX-Icon-1920x-margin-2x.png";

        public string CoverIconFileName { get; set; } = "KitX-Icon-Background.png";

        public string AppLanguage { get; set; } = "zh-cn";

        public string Theme { get; set; } = "Follow";

        public string ThemeColor { get; set; } = "#FF3873D9";

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

        public string LocalPluginsFileFolder { get; set; } = "./Plugins/";

        public string LocalPluginsDataFolder { get; set; } = "./PluginsDatas/";

        public bool DeveloperSetting { get; set; } = false;

        public bool ShowAnnouncementWhenStart { get; set; } = true;

        public ulong RanTime { get; set; } = 0;

        public int LastBreakAfterExit { get; set; } = 2000;
    }

    public class Config_Windows
    {
        public Config_MainWindow MainWindow { get; set; } = new();

        public Config_AnnouncementWindow AnnouncementWindow { get; set; } = new();

        public class Config_MainWindow : IWindowConfig
        {
            public Resolution Size { get; set; } = Resolution.Parse("1280x720");

            public Distances Location { get; set; } = new(left: -1, top: -1);

            public WindowState WindowState { get; set; } = WindowState.Normal;

            public bool IsHidden { get; set; } = false;

            public Dictionary<string, string> Tags { get; set; } = new()
            {
                { "SelectedPage", "Page_Home" }
            };

            public bool EnabledMica { get; set; } = true;

            public int GreetingTextCount_Morning { get; set; } = 5;

            public int GreetingTextCount_Noon { get; set; } = 3;

            public int GreetingTextCount_AfterNoon { get; set; } = 3;

            public int GreetingTextCount_Evening { get; set; } = 2;

            public int GreetingTextCount_Night { get; set; } = 4;

            public int GreetingUpdateInterval { get; set; } = 10;
        }

        public class Config_AnnouncementWindow : IWindowConfig
        {
            public Resolution Size { get; set; } = Resolution.Parse("1280x720");

            public Distances Location { get; set; } = new(left: -1, top: -1);
        }
    }

    public class Config_Pages
    {
        public Config_HomePage Home { get; set; } = new();

        public Config_DevicePage Device { get; set; } = new();

        public Config_MarketPage Market { get; set; } = new();

        public Config_SettingsPage Settings { get; set; } = new();

        public class Config_HomePage
        {
            public NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; } = NavigationViewPaneDisplayMode.Auto;

            public string SelectedViewName { get; set; } = "View_Recent";

            public bool IsNavigationViewPaneOpened { get; set; } = true;

            public bool UseAreaExpanded { get; set; } = true;
        }

        public class Config_DevicePage
        {

        }

        public class Config_MarketPage
        {

        }

        public class Config_SettingsPage
        {
            public NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; } = NavigationViewPaneDisplayMode.Auto;

            public string SelectedViewName { get; set; } = "View_General";

            public bool PaletteAreaExpanded { get; set; } = false;

            public bool WebRelatedAreaExpanded { get; set; } = true;

            public bool WebRelatedAreaOfNetworkInterfacesExpanded { get; set; } = false;

            public bool LogRelatedAreaExpanded { get; set; } = true;

            public bool UpdateRelatedAreaExpanded { get; set; } = true;

            public bool AboutAreaExpanded { get; set; } = false;

            public bool AuthorsAreaExpanded { get; set; } = false;

            public bool LinksAreaExpanded { get; set; } = false;

            public bool ThirdPartyLicensesAreaExpanded { get; set; } = false;

            public bool IsNavigationViewPaneOpened { get; set; } = true;
        }
    }

    public class Config_Web
    {
        public double DelayStartSeconds { get; set; } = 0.5;

        public string ApiServer { get; set; } = "api.catrol.cn";

        public string ApiPath { get; set; } = "/apps/kitx/";

        public int DevicesViewRefreshDelay { get; set; } = 1000;

        public List<string>? AcceptedNetworkInterfaces { get; set; } = null;

        public int? UserSpecifiedDevicesServerPort { get; set; } = null;

        public int? UserSpecifiedPluginsServerPort { get; set; } = null;

        public int UdpPortSend { get; set; } = 23404;

        public int UdpPortReceive { get; set; } = 24040;

        public int UdpSendFrequency { get; set; } = 1000;

        public string UdpBroadcastAddress { get; set; } = "224.0.0.0";

        public string IPFilter { get; set; } = "192.168";

        public int SocketBufferSize { get; set; } = 1024 * 100;

        public int DeviceInfoTTLSeconds { get; set; } = 7;

        public bool DisableRemovingOfflineDeviceCard { get; set; } = false;

        public string UpdateServer { get; set; } = "api.catrol.cn";

        public string UpdatePath { get; set; } = "/apps/kitx/%platform%/";

        public string UpdateDownloadPath { get; set; } = "/apps/kitx/update/%platform%/";

        /// <summary>
        /// %channel%   -   Stable, Beta, Alpha (stable, beta, alpha)
        /// </summary>

        public string UpdateChannel { get; set; } = "stable";

        public string UpdateSource { get; set; } = "latest-components.json";

        public int DebugServicesServerPort { get; set; } = 7777;
    }

    public class Config_Log
    {
        public long LogFileSingleMaxSize { get; set; } = 1024 * 1024 * 10;      //  10MB

        public string LogFilePath { get; set; } = "./Log/";

        public string LogTemplate { get; set; } = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        public int LogFileMaxCount { get; set; } = 50;

        public int LogFileFlushInterval { get; set; } = 30;

#if DEBUG

        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;

#else

        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Warning;

#endif

    }

    public class Config_IO
    {
        public int UpdatingCheckPerThreadFilesCount { get; set; } = 20;

        public int OperatingSystemVersionUpdateInterval { get; set; } = 60;
    }

    public class Config_Activity
    {
        public int TotalRecorded { get; set; } = 0;
    }

    public class Config_Loaders
    {
        public string InstallPath { get; set; } = "./Loaders/";
    }
}
