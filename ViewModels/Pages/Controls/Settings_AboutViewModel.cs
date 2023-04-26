﻿using Common.BasicHelper.IO;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using System.ComponentModel;
using System.Reflection;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class Settings_AboutViewModel : ViewModelBase, INotifyPropertyChanged
{
    internal Settings_AboutViewModel()
    {
        InitCommands();
    }

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitCommands()
    {
        AppNameButtonClickedCommand = new(AppNameButtonClicked);

        LoadThirdPartLicenseCommand = new(LoadThirdPartLicense);
    }

    /// <summary>
    /// 保存对配置文件的修改
    /// </summary>
    private static void SaveChanges()
    {
        EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
    }

    /// <summary>
    /// 版本号属性
    /// </summary>
    internal static string VersionText => $"v{Assembly.GetEntryAssembly().GetName().Version}";

    internal bool easterEggsFounded = false;

    /// <summary>
    /// 制作人员列表属性
    /// </summary>
    internal bool EasterEggsFounded
    {
        get => easterEggsFounded;
        set
        {
            easterEggsFounded = value;
            PropertyChanged?.Invoke(this, new(nameof(EasterEggsFounded)));
        }
    }

    internal string thirdPartLicenseString = string.Empty;

    internal string ThirdPartLicenseString
    {
        get => thirdPartLicenseString;
        set
        {
            thirdPartLicenseString = value;
            PropertyChanged?.Invoke(this, new(nameof(ThirdPartLicenseString)));
        }
    }

    /// <summary>
    /// 关于区域是否展开
    /// </summary>
    public static bool AboutAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.AboutAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.AboutAreaExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 作者列表区域是否展开
    /// </summary>
    public static bool AuthorsAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.AuthorsAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.AuthorsAreaExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 链接区域是否展开
    /// </summary>
    public static bool LinksAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.LinksAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.LinksAreaExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 第三方声明区域是否展开
    /// </summary>
    public static bool ThirdPartyLicensesAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded = value;
            SaveChanges();
        }
    }

    internal int clickCount = 0;

    /// <summary>
    /// 应用名称按钮单击命令
    /// </summary>
    internal DelegateCommand? AppNameButtonClickedCommand { get; set; }

    /// <summary>
    /// 读取第三方说明的按钮单击命令
    /// </summary>
    internal DelegateCommand? LoadThirdPartLicenseCommand { get; set; }

    private void AppNameButtonClicked(object _) => ++clickCount;

    private async void LoadThirdPartLicense(object _)
    {
        string license = await FileHelper.ReadAllAsync(Data.GlobalInfo.ThirdPartLicenseFilePath);
        ThirdPartLicenseString = license;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
}

#pragma warning restore CS8602 // 解引用可能出现空引用。

//                                     __
//                              ___  _// \
//                            _/   \/__|_ \
//                           /  __//_/==\_| ___
//                         / | / /|// == \ \   /
//                         |  | |\|| //_\ | |_/
//                          \  \ \\ / \_/| || \
//                           \___/\\| _  ///___\
//                             \__|\_\=//_// _\_|
//                                \___\_____/
//                               !! \____/
//                              !!
//                               !!
//                    ___      -(!!      __ ___ _
//                   |\|  \       !!_.-~~ /|\-  \~-._
//                   | -\| |      !!/   /  | |\- | |\ \
//                    \__-\|______ !!  |    \___\|  \_\|
//              _____ _.-~/|\     \\!!  \  |  /       ~-.
//            /     /|  / /|  \    \!!    \ /          |\~-
//          /  ---/| | |   |\  |     !!                 \__|
//         | ---/| | |  \ /|  /    -(!!
//         | -/| |  /     \|/        !!
//         |/____ /                  !!)-
//                                   !!
