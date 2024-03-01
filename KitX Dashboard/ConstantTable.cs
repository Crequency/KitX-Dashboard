﻿using System;
using Common.BasicHelper.Utils.Extensions;

namespace KitX.Dashboard;

internal static class ConstantTable
{
    internal const string AppName = "KitX";

    internal const string AppFullName = "KitX Dashboard";

    internal const string DataPath = "./Data/";

    internal const string LanguageFilePath = "./Languages/";

    internal const string AssetsPath = "./Assets/";

    internal const string KXPTempReleasePath = "Temp/";

    internal const string UpdateSavePath = "./Update/";

    internal const string IconBase64FileName = "KitX.Base64.txt";

    private const string activitiesDataBaseFilePath = $"{DataPath}Activities.db";

    private const string thirdPartLicenseFilePath = $"{AssetsPath}ThirdPartLicense.md";

    internal static string ActivitiesDataBaseFilePath => activitiesDataBaseFilePath.GetFullPath();

    internal static string ThirdPartLicenseFilePath => thirdPartLicenseFilePath.GetFullPath();

    internal static int DevicesServerPort = -1;

    internal static int PluginsServerPort = -1;

    internal static bool Running = true;

    internal static bool Exiting = false;

    internal static bool IsMainMachine = false;

    internal static string? MainMachineAddress;

    internal static int MainMachinePort = -1;

    internal static bool SkipNetworkSystemOnStartup = false;

    internal static DateTime ServerBuildTime = new();

    internal const string Api_Get_Announcements = "get-announcements.php";

    internal const string Api_Get_Announcement = "get-announcement.php";

    internal static string MyMacAddress = string.Empty;

    internal static string KitXIconBase64 = string.Empty;

    internal static bool IsSingleProcessStartMode = true;

    internal static bool EnabledConfigFileHotReload = true;
}
