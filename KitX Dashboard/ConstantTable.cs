using System;
using Common.BasicHelper.Utils.Extensions;

namespace KitX.Dashboard;

/// <summary>
/// Dashboard-specific constants.
/// General constants are delegated to <see cref="KitX.Core.ConstantTable"/>.
/// </summary>
internal static class ConstantTable
{
    // ──────────────────────────────────────────────
    //  Delegated to KitX.Core.ConstantTable
    // ──────────────────────────────────────────────

    internal const string AppName = KitX.Core.ConstantTable.AppName;

    internal const string AppFullName = KitX.Core.ConstantTable.AppFullName;

    internal const string DataPath = KitX.Core.ConstantTable.DataPath;

    internal const string LanguageFilePath = KitX.Core.ConstantTable.LanguageFilePath;

    internal const string AssetsPath = KitX.Core.ConstantTable.AssetsPath;

    internal const string UpdateSavePath = KitX.Core.ConstantTable.UpdateSavePath;

    internal const string IconBase64FileName = KitX.Core.ConstantTable.IconBase64FileName;

    internal static string ActivitiesDataBaseFilePath => KitX.Core.ConstantTable.ActivitiesDataBaseFilePath;

    internal static string ThirdPartyLicenseFilePath => KitX.Core.ConstantTable.ThirdPartyLicenseFilePath;

    internal static int DevicesServerPort
    {
        get => KitX.Core.ConstantTable.DevicesServerPort;
        set => KitX.Core.ConstantTable.DevicesServerPort = value;
    }

    internal static int PluginsServerPort
    {
        get => KitX.Core.ConstantTable.PluginsServerPort;
        set => KitX.Core.ConstantTable.PluginsServerPort = value;
    }

    internal static bool Running
    {
        get => KitX.Core.ConstantTable.Running;
        set => KitX.Core.ConstantTable.Running = value;
    }

    internal static bool Exiting
    {
        get => KitX.Core.ConstantTable.Exiting;
        set => KitX.Core.ConstantTable.Exiting = value;
    }

    internal static bool Restarting
    {
        get => KitX.Core.ConstantTable.Restarting;
        set => KitX.Core.ConstantTable.Restarting = value;
    }

    internal static bool EnsureExiting
    {
        get => KitX.Core.ConstantTable.EnsureExiting;
        set => KitX.Core.ConstantTable.EnsureExiting = value;
    }

    internal static bool IsMainMachine
    {
        get => KitX.Core.ConstantTable.IsMainMachine;
        set => KitX.Core.ConstantTable.IsMainMachine = value;
    }

    internal static string? MainMachineAddress
    {
        get => KitX.Core.ConstantTable.MainMachineAddress;
        set => KitX.Core.ConstantTable.MainMachineAddress = value;
    }

    internal static int MainMachinePort
    {
        get => KitX.Core.ConstantTable.MainMachinePort;
        set => KitX.Core.ConstantTable.MainMachinePort = value;
    }

    internal static bool SkipNetworkSystemOnStartup
    {
        get => KitX.Core.ConstantTable.SkipNetworkSystemOnStartup;
        set => KitX.Core.ConstantTable.SkipNetworkSystemOnStartup = value;
    }

    internal static DateTime ServerBuildTime
    {
        get => KitX.Core.ConstantTable.ServerBuildTime;
        set => KitX.Core.ConstantTable.ServerBuildTime = value;
    }

    internal static string KitXIconBase64
    {
        get => KitX.Core.ConstantTable.KitXIconBase64;
        set => KitX.Core.ConstantTable.KitXIconBase64 = value;
    }

    internal static bool IsSingleProcessStartMode
    {
        get => KitX.Core.ConstantTable.IsSingleProcessStartMode;
        set => KitX.Core.ConstantTable.IsSingleProcessStartMode = value;
    }

    internal static bool EnabledConfigFileHotReload
    {
        get => KitX.Core.ConstantTable.EnabledConfigFileHotReload;
        set => KitX.Core.ConstantTable.EnabledConfigFileHotReload = value;
    }
}
