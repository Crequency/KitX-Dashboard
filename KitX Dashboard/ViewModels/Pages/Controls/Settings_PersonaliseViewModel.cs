using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Media;
using KitX.Dashboard.Data;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using MsBox.Avalonia;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_PersonaliseViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    internal Settings_PersonaliseViewModel()
    {
        InitCommands();

        InitEvents();

        InitData();
    }

    private void InitCommands()
    {
        ColorConfirmedCommand = ReactiveCommand.Create(async () =>
        {
            var c = themeColor;

            await Dispatcher.UIThread.InvokeAsync(() =>
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

            ConfigManager.AppConfig.App.ThemeColor = themeColor.ToHexString();

            SaveAppConfigChanges();
        });

        MicaOpacityConfirmedCommand = ReactiveCommand.Create(SaveAppConfigChanges);

        MicaToolTipClosedCommand = ReactiveCommand.Create(() => MicaToolTipIsOpen = false);
    }

    private void InitEvents()
    {
        EventService.DevelopSettingsChanged += () =>
        {
            MicaOpacityConfirmButtonVisibility = ConfigManager.AppConfig.App.DeveloperSetting;
        };

        EventService.LanguageChanged += () =>
        {
            foreach (var item in SupportedThemes)
                item.ThemeDisplayName = GetThemeDisplayText(item.ThemeName);

            _currentAppTheme = SupportedThemes.Find(
                x => x.ThemeName.Equals(ConfigManager.AppConfig.App.Theme)
            );

            PropertyChanged?.Invoke(
                this,
                new(nameof(CurrentAppTheme))
            );
        };
    }

    private void InitData()
    {
        SupportedLanguages.Clear();

        foreach (var item in ConfigManager.AppConfig.App.SurpportLanguages)
            SupportedLanguages.Add(new SupportedLanguage()
            {
                LanguageCode = item.Key,
                LanguageName = item.Value
            });

        LanguageSelected = SupportedLanguages.FindIndex(
            x => x.LanguageCode.Equals(ConfigManager.AppConfig.App.AppLanguage)
        );
    }

    private Color2 themeColor = new();

    internal Color2 ThemeColor
    {
        get
        {
            var obj = Application.Current?.Resources["ThemePrimaryAccent"];

            if (obj is not SolidColorBrush brush) return new();

            return new(brush.Color);
        }
        set => themeColor = value;
    }

    private static string GetThemeDisplayText(string key) => FetchStringFromResource(
        Application.Current,
        key,
        prefix: "Text_Settings_Personalise_Theme_"
    ) ?? string.Empty;

    internal static List<SupportedTheme> SupportedThemes { get; } = new()
    {
        new()
        {
            ThemeName = FluentAvaloniaTheme.LightModeString,
            ThemeDisplayName = GetThemeDisplayText(FluentAvaloniaTheme.LightModeString),
        },
        new()
        {
            ThemeName = FluentAvaloniaTheme.DarkModeString,
            ThemeDisplayName = GetThemeDisplayText(FluentAvaloniaTheme.DarkModeString),
        },
        new()
        {
            ThemeName = FluentAvaloniaTheme.HighContrastModeString,
            ThemeDisplayName = GetThemeDisplayText(FluentAvaloniaTheme.HighContrastModeString),
        },
        new()
        {
            ThemeName = "Follow",
            ThemeDisplayName = GetThemeDisplayText("Follow"),
        }
    };

    private SupportedTheme? _currentAppTheme = SupportedThemes.Find(
        x => x.ThemeName.Equals(ConfigManager.AppConfig.App.Theme)
    );

    internal SupportedTheme? CurrentAppTheme
    {
        get => _currentAppTheme;
        set
        {
            _currentAppTheme = value;

            if (value is null) return;

            ConfigManager.AppConfig.App.Theme = value.ThemeName;

            if (Application.Current is null) return;

            Application.Current.RequestedThemeVariant ??= value.ThemeName == "Follow" ? ThemeVariant.Default : value.ThemeName switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

            EventService.Invoke(nameof(EventService.ThemeConfigChanged));

            SaveAppConfigChanges();
        }
    }

    internal List<SupportedLanguage> SupportedLanguages { get; } = new();

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
                    File.ReadAllText($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary ?? new()
            );
        }
        catch (Exception ex)
        {
            MessageBoxManager.GetMessageBoxStandard(
                "Error",
                "No this language file.",
                icon: MsBox.Avalonia.Enums.Icon.Error
            ).ShowWindowAsync();

            Log.Warning(ex, $"In {location}: Language File {lang}.axaml not found.");
        }

        EventService.Invoke(nameof(EventService.LanguageChanged));
    }

    internal int languageSelected = -1;

    internal int LanguageSelected
    {
        get => languageSelected;
        set
        {
            try
            {
                ConfigManager.AppConfig.App.AppLanguage = SupportedLanguages[value].LanguageCode;

                if (languageSelected != -1) LoadLanguage();

                languageSelected = value;

                SaveAppConfigChanges();
            }
            catch
            {
                languageSelected = 0;
            }
        }
    }

    internal static bool MicaAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.MicaAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.MicaAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static int MicaStatus
    {
        get => ConfigManager.AppConfig.Windows.MainWindow.EnabledMica ? 0 : 1;
        set
        {
            ConfigManager.AppConfig.Windows.MainWindow.EnabledMica = value != 1;
            SaveAppConfigChanges();
        }
    }

    internal static double MicaOpacity
    {
        get => ConfigManager.AppConfig.Windows.MainWindow.MicaOpacity;
        set
        {
            ConfigManager.AppConfig.Windows.MainWindow.MicaOpacity = value;
            EventService.Invoke(nameof(EventService.MicaOpacityChanged));
        }
    }

    internal static bool MicaToolTipIsOpen
    {
        get => ConfigManager.AppConfig.Pages.Settings.MicaToolTipIsOpen;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.MicaToolTipIsOpen = value;
            SaveAppConfigChanges();
        }
    }

    internal bool MicaOpacityConfirmButtonVisibility
    {
        get => ConfigManager.AppConfig.App.DeveloperSetting;
        set => PropertyChanged?.Invoke(
            this,
            new(nameof(MicaOpacityConfirmButtonVisibility))
        );
    }

    internal static bool PaletteAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.PaletteAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.PaletteAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, Task>? ColorConfirmedCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MicaOpacityConfirmedCommand { get; set; }

    internal ReactiveCommand<Unit, bool>? MicaToolTipClosedCommand { get; set; }
}
