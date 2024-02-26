using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Media;
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

    public override void InitCommands()
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

            ConfigManager.Instance.AppConfig.App.ThemeColor = themeColor.ToHexString();

            SaveAppConfigChanges();
        });

        MicaOpacityConfirmedCommand = ReactiveCommand.Create(SaveAppConfigChanges);

        MicaToolTipClosedCommand = ReactiveCommand.Create(() => MicaToolTipIsOpen = false);
    }

    public override void InitEvents()
    {
        EventService.DevelopSettingsChanged += () =>
        {
            MicaOpacityConfirmButtonVisibility = ConfigManager.Instance.AppConfig.App.DeveloperSetting;
        };

        EventService.LanguageChanged += () =>
        {
            foreach (var item in SupportedThemes)
                item.ThemeDisplayName = GetThemeDisplayText(item.ThemeName);

            _currentAppTheme = SupportedThemes.Find(
                x => x.ThemeName.Equals(ConfigManager.Instance.AppConfig.App.Theme)
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

        foreach (var item in ConfigManager.Instance.AppConfig.App.SurpportLanguages)
            SupportedLanguages.Add(new SupportedLanguage()
            {
                LanguageCode = item.Key,
                LanguageName = item.Value
            });

        LanguageSelected = SupportedLanguages.FindIndex(
            x => x.LanguageCode.Equals(ConfigManager.Instance.AppConfig.App.AppLanguage)
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

    private static string GetThemeDisplayText(string key) => Translate(key, prefix: "Text_Settings_Personalise_Theme_") ?? string.Empty;

    internal static List<SupportedTheme> SupportedThemes { get; } =
    [
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
            ThemeName = "Follow",
            ThemeDisplayName = GetThemeDisplayText("Follow"),
        }
    ];

    private SupportedTheme? _currentAppTheme = SupportedThemes.Find(
        x => x.ThemeName.Equals(ConfigManager.Instance.AppConfig.App.Theme)
    );

    internal SupportedTheme? CurrentAppTheme
    {
        get => _currentAppTheme;
        set
        {
            _currentAppTheme = value;

            if (value is null) return;

            ConfigManager.Instance.AppConfig.App.Theme = value.ThemeName;

            if (Application.Current is null) return;

            Application.Current.RequestedThemeVariant = value.ThemeName switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

            EventService.Invoke(nameof(EventService.ThemeConfigChanged));

            SaveAppConfigChanges();
        }
    }

    internal List<SupportedLanguage> SupportedLanguages { get; } = [];

    internal static void LoadLanguage()
    {
        var location = $"{nameof(Settings_PersonaliseViewModel)}.{nameof(LoadLanguage)}";

        var lang = ConfigManager.Instance.AppConfig.App.AppLanguage;

        if (Application.Current is null) return;

        try
        {
            Application.Current.Resources.MergedDictionaries.Clear();

            Application.Current.Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    File.ReadAllText($"{ConstantTable.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary ?? []
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
                ConfigManager.Instance.AppConfig.App.AppLanguage = SupportedLanguages[value].LanguageCode;

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
        get => ConfigManager.Instance.AppConfig.Pages.Settings.MicaAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.MicaAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal static int MicaStatus
    {
        get => ConfigManager.Instance.AppConfig.Windows.MainWindow.EnabledMica ? 0 : 1;
        set
        {
            ConfigManager.Instance.AppConfig.Windows.MainWindow.EnabledMica = value != 1;
            SaveAppConfigChanges();
        }
    }

    internal static bool MicaToolTipIsOpen
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.MicaToolTipIsOpen;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.MicaToolTipIsOpen = value;
            SaveAppConfigChanges();
        }
    }

    internal bool MicaOpacityConfirmButtonVisibility
    {
        get => ConfigManager.Instance.AppConfig.App.DeveloperSetting;
        set => PropertyChanged?.Invoke(
            this,
            new(nameof(MicaOpacityConfirmButtonVisibility))
        );
    }

    internal static bool PaletteAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.PaletteAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.PaletteAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, Task>? ColorConfirmedCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MicaOpacityConfirmedCommand { get; set; }

    internal ReactiveCommand<Unit, bool>? MicaToolTipClosedCommand { get; set; }
}
