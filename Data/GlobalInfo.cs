using System;
using System.IO;

namespace KitX_Dashboard.Data
{
    internal static class GlobalInfo
    {
        internal const string AppName = "KitX";

        internal const string AppFullName = "KitX Dashboard";

        internal const string ConfigPath = "./Config/";

        internal const string DataPath = "./Data/";

        internal const string LanguageFilePath = "./Languages/";

        internal const string AssetsPath = "./Assets/";

        internal const string KXPTempReleasePath = "Temp/";

        internal const string UpdateSavePath = "./Update/";

        internal const string configFilePath = $"{ConfigPath}config.json";

        internal const string pluginsListConfigFilePath = $"{ConfigPath}plugins.json";

        internal const string activitiesDataBaseFilePath = $"{DataPath}Activities.db";

        internal const string thirdPartLicenseFilePath = $"{AssetsPath}ThirdPartLicense.md";

        internal const string IconBase64FileName = "KitX.Base64.txt";

        internal const int LastBreakAfterExit = 2000;

        internal static string ConfigFilePath => Path.GetFullPath(configFilePath);

        internal static string PluginsListConfigFilePath => Path.GetFullPath(pluginsListConfigFilePath);

        internal static string ActivitiesDataBaseFilePath => Path.GetFullPath(activitiesDataBaseFilePath);

        internal static string ThirdPartLicenseFilePath => Path.GetFullPath(thirdPartLicenseFilePath);

        internal static int PluginServerPort = -1;

        internal static bool Running = true;

        internal static bool Exiting = false;

        internal static bool IsMainMachine = false;

        internal static int DeviceServerPort = -1;

        internal static DateTime ServerBuildTime = new();

        internal const string Api_Get_Announcements = "get-announcements.php";

        internal const string Api_Get_Announcement = "get-announcement.php";

        internal const string AnnouncementsJsonPath = $"{ConfigPath}announcements.json";

        internal static string MyMacAddress = string.Empty;

        internal static string KitXIconBase64 = string.Empty;

        internal static bool IsSingleProcessStartMode = true;

        internal static bool EnabledConfigFileHotReload = true;
    }
}

//
//                                     ________________________
//                         ,---------+/       +----------+     \
//                       /          ||        |          |      |
//                     /            ||        +----------+      |
//    _________------=--<I|---------+----------------------------,
//  .----=============|=========---=|=======================-->> |
//  |     ______      |             |              ______        |
// [|    / _--_ \     /             |             / _--_ \       ]
//   \__|| -__- ||___/_____________/_____________|| -__- ||_____/
//        \____/                                   \____/
//
