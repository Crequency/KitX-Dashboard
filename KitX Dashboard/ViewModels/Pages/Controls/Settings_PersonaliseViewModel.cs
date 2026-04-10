using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.Event;
using KitX.Dashboard;
using KitX.Dashboard.Models;
using MsBox.Avalonia;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_PersonaliseViewModel : ViewModelBase
{
    private readonly IConfigService _configService;

    internal Settings_PersonaliseViewModel()
    {
        _configService = ConfigService;

        InitCommands();

        InitEvents();

        InitData();
    }

    public sealed override void InitCommands()
    {
        ColorConfirmedCommand = ReactiveCommand.Create(async () =>
        {
            var c = themeColor;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Application.Current is null)
                    return;

                Application.Current.Resources["ThemePrimaryAccent"] = new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));

                for (char i = 'A'; i <= 'E'; ++i)
                    Application.Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] = new SolidColorBrush(
                        new Color((byte)(170 + (i - 'A') * 17), c.R, c.G, c.B)
                    );

                for (int i = 1; i <= 9; ++i)
                    Application.Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] = new SolidColorBrush(
                        new Color((byte)(i * 10 + i), c.R, c.G, c.B)
                    );
            });

            _configService.AppConfig.App.ThemeColor = themeColor.ToString();

            _configService.SaveAll();
        });
    }

    public sealed override void InitEvents()
    {
        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.LanguageChanged, (s, e) =>
        {
            // Update theme display names
            foreach (var item in SupportedThemes)
                item.ThemeDisplayName = GetThemeDisplayText(item.ThemeName);

            _currentAppTheme = SupportedThemes.FirstOrDefault(x => x.ThemeName.Equals(_configService.AppConfig.App.Theme));

            // Update language display names
            foreach (var item in SupportedLanguages)
                item.LanguageName = GetLanguageDisplayText(item.LanguageCode);

            this.RaisePropertyChanged(nameof(CurrentAppTheme));
        });
    }

    private void InitData()
    {
        SupportedLanguages.Clear();

        foreach (var item in _configService.AppConfig.App.SurpportLanguages)
            SupportedLanguages.Add(new SupportedLanguage() { LanguageCode = item.Key, LanguageName = item.Value });

        var selectedLanguage = SupportedLanguages.FirstOrDefault(x => x.LanguageCode.Equals(_configService.AppConfig.App.AppLanguage));
        LanguageSelected = selectedLanguage != null ? SupportedLanguages.IndexOf(selectedLanguage) : 0;

        // Initialize current theme - must explicitly set to show in ComboBox
        _currentAppTheme = SupportedThemes.FirstOrDefault(x => x.ThemeName.Equals(_configService.AppConfig.App.Theme))
            ?? SupportedThemes.FirstOrDefault(); // Fallback to first theme if not found
    }

    private Color themeColor = new();

    internal Color ThemeColor
    {
        get
        {
            var obj = Application.Current?.Resources["ThemePrimaryAccent"];

            if (obj is not SolidColorBrush brush)
                return new();

            return brush.Color;
        }
        set => themeColor = value;
    }

    private static string GetThemeDisplayText(string key) => Translate(key, prefix: "Text_Settings_Personalise_Theme_") ?? string.Empty;

    private string GetLanguageDisplayText(string languageCode) =>
        _configService.AppConfig.App.SurpportLanguages.TryGetValue(languageCode, out var name) ? name : string.Empty;

    // Use static field instead of property to ensure consistent object references for ComboBox binding
    private static readonly List<SupportedTheme> _supportedThemes =
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
        new() { ThemeName = "Follow", ThemeDisplayName = GetThemeDisplayText("Follow") },
    ];

    internal static List<SupportedTheme> SupportedThemes => _supportedThemes;

    private SupportedTheme? _currentAppTheme;

    internal SupportedTheme? CurrentAppTheme
    {
        get => _currentAppTheme ??= SupportedThemes.Find(x => x.ThemeName.Equals(_configService.AppConfig.App.Theme));
        set
        {
            _currentAppTheme = value;

            if (value is null)
                return;

            _configService.AppConfig.App.Theme = value.ThemeName;

            if (Application.Current is null)
                return;

            Application.Current.RequestedThemeVariant = value.ThemeName switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

            var eventService = App.GetService<IEventService>();
            eventService.Publish(EventNames.ThemeConfigChanged, EventArgs.Empty);

            _configService.SaveAll();
        }
    }

    internal ObservableCollection<SupportedLanguage> SupportedLanguages { get; } = [];

    internal static void LoadLanguage()
    {
        const string location = $"{nameof(Settings_PersonaliseViewModel)}.{nameof(LoadLanguage)}";

        var configService = App.GetService<IConfigService>();
        var lang = configService.AppConfig.App.AppLanguage;

        if (Application.Current is null)
            return;

        try
        {
            Application.Current.Resources.MergedDictionaries.Clear();

            Application.Current.Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(File.ReadAllText($"{ConstantTable.LanguageFilePath}/{lang}.axaml")) as ResourceDictionary
                    ?? []
            );
        }
        catch (Exception ex)
        {
            MessageBoxManager
                .GetMessageBoxStandard("Error", "No this language file.", icon: MsBox.Avalonia.Enums.Icon.Error)
                .ShowWindowAsync();

            Log.Warning(ex, $"In {location}: Language File {lang}.axaml not found.");
        }

        var eventService = App.GetService<IEventService>();
        eventService.Publish(EventNames.LanguageChanged, EventArgs.Empty);
    }

    internal int languageSelected = -1;

    internal int LanguageSelected
    {
        get => languageSelected;
        set
        {
            try
            {
                _configService.AppConfig.App.AppLanguage = SupportedLanguages[value].LanguageCode;

                if (languageSelected != -1)
                    LoadLanguage();

                languageSelected = value;

                _configService.SaveAll();
            }
            catch
            {
                languageSelected = 0;
            }
        }
    }

    internal bool PaletteAreaExpanded
    {
        get => _configService.AppConfig.Pages.Settings.PaletteAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.PaletteAreaExpanded = value;

            _configService.SaveAll();
        }
    }

    internal ReactiveCommand<Unit, Task>? ColorConfirmedCommand { get; set; }
}
