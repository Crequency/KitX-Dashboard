using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Common.BasicHelper.IO;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Media;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Models;
using KitX_Dashboard.Services;
using MessageBox.Avalonia;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class Settings_PersonaliseViewModel : ViewModelBase, INotifyPropertyChanged
{
    internal Settings_PersonaliseViewModel()
    {
        InitCommands();

        InitEvent();

        InitData();
    }

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitCommands()
    {
        ColorConfirmedCommand = new(ColorConfirmed);

        MicaOpacityConfirmedCommand = new(MicaOpacityConfirmed);

        MicaToolTipClosedCommand = new(MicaToolTipClosed);
    }

    /// <summary>
    /// 初始化事件
    /// </summary>
    private void InitEvent()
    {
        EventService.DevelopSettingsChanged += () =>
        {
            MicaOpacityConfirmButtonVisibility = ConfigManager.AppConfig.App.DeveloperSetting;
        };
        EventService.LanguageChanged += () =>
        {
            foreach (var item in SurpportThemes)
                item.ThemeDisplayName = GetThemeInLanguages(item.ThemeName);
            _currentAppTheme = SurpportThemes.Find(
                x => x.ThemeName.Equals(ConfigManager.AppConfig.App.Theme));
            PropertyChanged?.Invoke(this, new(nameof(CurrentAppTheme)));
        };
    }

    /// <summary>
    /// 初始化数据
    /// </summary>
    private void InitData()
    {
        SurpportLanguages.Clear();
        foreach (var item in ConfigManager.AppConfig.App.SurpportLanguages)
        {
            SurpportLanguages.Add(new SurpportLanguages()
            {
                LanguageCode = item.Key,
                LanguageName = item.Value
            });
        }
        LanguageSelected = SurpportLanguages.FindIndex(
            x => x.LanguageCode.Equals(ConfigManager.AppConfig.App.AppLanguage));
    }

    /// <summary>
    /// 保存变更
    /// </summary>
    private static void SaveChanges()
    {
        EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
    }

    private Color2 nowColor = new();

    /// <summary>
    /// 主题色属性
    /// </summary>
    internal Color2 ThemeColor
    {
        get
        {
            var obj = Application.Current?.Resources["ThemePrimaryAccent"];

            if (obj is not SolidColorBrush brush) return new();

            return new(brush.Color);
        }

        set => nowColor = value;
    }

    /// <summary>
    /// 获取主题不同语言的表示方式
    /// </summary>
    /// <param name="key">语言键</param>
    /// <returns>表示方式</returns>
    private static string GetThemeInLanguages(string key)
    {
        if (Application.Current is null) return string.Empty;

        var namePrefix = "Text_Settings_Personalise_Theme";

        _ = Application.Current.TryFindResource($"{namePrefix}_{key}", out var result);

        var str = result as string;

        return str is not null ? str : string.Empty;
    }

    /// <summary>
    /// 可选的应用主题属性
    /// </summary>
    internal static List<SurpportTheme> SurpportThemes { get; } = new()
    {
        new()
        {
            ThemeName = FluentAvaloniaTheme.LightModeString,
            ThemeDisplayName = GetThemeInLanguages(FluentAvaloniaTheme.LightModeString),
        },
        new()
        {
            ThemeName = FluentAvaloniaTheme.DarkModeString,
            ThemeDisplayName = GetThemeInLanguages(FluentAvaloniaTheme.DarkModeString),
        },
        new()
        {
            ThemeName = FluentAvaloniaTheme.HighContrastModeString,
            ThemeDisplayName = GetThemeInLanguages(FluentAvaloniaTheme.HighContrastModeString),
        },
        new()
        {
            ThemeName = "Follow",
            ThemeDisplayName = GetThemeInLanguages("Follow"),
        }
    };

    private SurpportTheme? _currentAppTheme = SurpportThemes.Find(
        x => x.ThemeName.Equals(ConfigManager.AppConfig.App.Theme));

    /// <summary>
    /// 当前应用主题属性
    /// </summary>
    internal SurpportTheme? CurrentAppTheme
    {
        get => _currentAppTheme;
        set
        {
            _currentAppTheme = value;

            if (value is null) return;

            ConfigManager.AppConfig.App.Theme = value.ThemeName;

            var faTheme = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();

            if (faTheme is null) return;

            faTheme.RequestedTheme = value.ThemeName == "Follow" ? null : value.ThemeName;

            EventService.Invoke(nameof(EventService.ThemeConfigChanged));

            SaveChanges();
        }
    }

    internal List<SurpportLanguages> SurpportLanguages { get; } = new();

    /// <summary>
    /// 加载语言
    /// </summary>
    internal static void LoadLanguage()
    {
        var location = $"{nameof(Settings_PersonaliseViewModel)}.{nameof(LoadLanguage)}";

        var lang = ConfigManager.AppConfig.App.AppLanguage;

        if (Application.Current is null) return;

        try
        {
            Application.Current.Resources.MergedDictionaries.Clear();

            Application.Current.Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary ?? new()
            );
        }
        catch (Exception ex)
        {
            MessageBoxManager.GetMessageBoxStandardWindow(
                "Error",
                "No this language file.",
                icon: MessageBox.Avalonia.Enums.Icon.Error
            ).Show();

            Log.Warning(ex, $"In {location}: Language File {lang}.axaml not found.");
        }

        EventService.Invoke(nameof(EventService.LanguageChanged));
    }

    internal int languageSelected = -1;

    /// <summary>
    /// 显示语言属性
    /// </summary>
    internal int LanguageSelected
    {
        get => languageSelected;
        set
        {
            try
            {
                ConfigManager.AppConfig.App.AppLanguage = SurpportLanguages[value].LanguageCode;

                if (languageSelected != -1) LoadLanguage();

                languageSelected = value;

                SaveChanges();
            }
            catch
            {
                languageSelected = 0;
            }
        }
    }

    /// <summary>
    /// Mica 效果设置项相关区域是否展开
    /// </summary>
    internal static bool MicaAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.MicaAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.MicaAreaExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// Mica 效果是否启用属性
    /// </summary>
    internal static int MicaStatus
    {
        get => ConfigManager.AppConfig.Windows.MainWindow.EnabledMica ? 0 : 1;
        set
        {
            ConfigManager.AppConfig.Windows.MainWindow.EnabledMica = value != 1;
            SaveChanges();
        }
    }

    /// <summary>
    /// Mica 效果透明度属性
    /// </summary>
    internal static double MicaOpacity
    {
        get => ConfigManager.AppConfig.Windows.MainWindow.MicaOpacity;
        set
        {
            ConfigManager.AppConfig.Windows.MainWindow.MicaOpacity = value;
            EventService.Invoke(nameof(EventService.MicaOpacityChanged));
        }
    }

    /// <summary>
    /// Mica 主题提示工具是否打开项
    /// </summary>
    internal static bool MicaToolTipIsOpen
    {
        get => ConfigManager.AppConfig.Pages.Settings.MicaToolTipIsOpen;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.MicaToolTipIsOpen = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// Mica 透明度确认按钮可见性
    /// </summary>
    internal bool MicaOpacityConfirmButtonVisibility
    {
        get => ConfigManager.AppConfig.App.DeveloperSetting;
        set => PropertyChanged?.Invoke(this,
            new(nameof(MicaOpacityConfirmButtonVisibility)));
    }

    /// <summary>
    /// 主题色调色盘设置项相关区域是否展开
    /// </summary>
    internal static bool PaletteAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.PaletteAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.PaletteAreaExpanded = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 确认主题色变更命令
    /// </summary>
    internal DelegateCommand? ColorConfirmedCommand { get; set; }

    /// <summary>
    /// 确认Mica主题透明度变更命令
    /// </summary>
    internal DelegateCommand? MicaOpacityConfirmedCommand { get; set; }

    /// <summary>
    /// Mica 提示工具关闭命令
    /// </summary>
    internal DelegateCommand? MicaToolTipClosedCommand { get; set; }

    private void ColorConfirmed(object? _)
    {
        var c = nowColor;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current is null) return;

            Application.Current.Resources["ThemePrimaryAccent"] = new SolidColorBrush(
                new Color(c.A, c.R, c.G, c.B)
            );

            for (char i = 'A'; i <= 'E'; ++i)
                Application.Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] =
                    new SolidColorBrush(
                        new Color((byte)(170 + (i - 'A') * 17), c.R, c.G, c.B)
                    );

            for (int i = 1; i <= 9; ++i)
                Application.Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] =
                    new SolidColorBrush(
                        new Color((byte)(i * 10 + i), c.R, c.G, c.B)
                    );
        });

        ConfigManager.AppConfig.App.ThemeColor = nowColor.ToHexString();

        SaveChanges();
    }

    private void MicaOpacityConfirmed(object? _) => SaveChanges();

    private void MicaToolTipClosed(object? _) => MicaToolTipIsOpen = false;

    public new event PropertyChangedEventHandler? PropertyChanged;
}

//                         __________________________
//                 __..--/".'                        '.
//         __..--""      | |                          |
//        /              | |                          |
//       /               | |    ___________________   |
//      ;                | |   :__________________/:  |
//      |                | |   |                 '.|  |
//      |                | |   |                  ||  |
//      |                | |   |                  ||  |
//      |                | |   |                  ||  |
//      |                | |   |                  ||  |
//      |                | |   |                  ||  |
//      |                | |   |                  ||  |
//      |                | |   |                  ||  |
//      |                | |   |______......-----"\|  |
//      |                | |   |_______......-----"   |
//      |                | |                          |
//      |                | |                          |
//      |                | |                  ____----|
//      |                | |_____.....----|#######|---|
//      |                | |______.....----""""       |
//      |                | |                          |
//      |. ..            | |   ,                      |
//      |... ....        | |  (c ----- """           .'
//      |..... ......  |\|_|    ____......------"""|"
//      |. .... .......| |""""""                   |
//      '... ..... ....| |                         |
//        "-._ .....  .| |                         |
//            "-._.....| |             ___...---"""'
//                "-._.| | ___...---"""
//                    """""
