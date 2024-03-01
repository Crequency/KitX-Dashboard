using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Serilog;

namespace KitX.Dashboard;

public partial class App : Application
{
    public static readonly Bitmap DefaultIcon = new(
        $"{ConstantTable.AssetsPath}{ConfigManager.Instance.AppConfig.App.CoverIconFileName}".GetFullPath()
    );

    private AppViewModel? viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        LoadTheme();

        LoadLanguage();

        CalculateThemeColor();

        InitializeLiveCharts();

        // Must construct after `LoadLanguage()` function.
        viewModel = new();

        DataContext = viewModel;
    }

    private void LoadTheme()
    {
        RequestedThemeVariant = ConfigManager.Instance.AppConfig.App.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            "Follow" => ThemeVariant.Default,
            _ => ThemeVariant.Default
        };
    }

    private void LoadLanguage()
    {
        var config = ConfigManager.Instance.AppConfig;
        var lang = config.App.AppLanguage;
        var backup_lang = config.App.SurpportLanguages.Keys.First();
        var path = $"{ConstantTable.LanguageFilePath}/{lang}.axaml".GetFullPath();
        var backup_langPath = $"{ConstantTable.LanguageFilePath}/{backup_lang}.axaml".GetFullPath();

        try
        {
            Resources.MergedDictionaries.Clear();

            Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    File.ReadAllText(path)
                ) as ResourceDictionary ?? []
            );
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Language File {lang}.axaml not found.");

            Resources.MergedDictionaries.Clear();

            try
            {
                Resources.MergedDictionaries.Add(
                    AvaloniaRuntimeXamlLoader.Load(
                        File.ReadAllText(backup_langPath)
                    ) as ResourceDictionary ?? []
                );

                config.App.AppLanguage = backup_lang;
            }
            catch (Exception e)
            {
                Log.Warning(e, $"Suspected absence of language files on record.");
            }
            finally
            {
                Log.Warning($"No surpport language file loaded.");
            }
        }

        try
        {
            EventService.Invoke(nameof(EventService.LanguageChanged));
        }
        catch (Exception e)
        {
            Log.Warning(e, $"Failed to invoke language changed event.");
        }
    }

    private static void CalculateThemeColor()
    {
        Color c = Color.Parse(ConfigManager.Instance.AppConfig.App.ThemeColor);

        if (Current is not null)
        {
            Current.Resources["ThemePrimaryAccent"] = new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));

            for (char i = 'A'; i <= 'E'; ++i)
            {
                Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] = new SolidColorBrush(
                    new Color((byte)(170 + (i - 'A') * 17), c.R, c.G, c.B)
                );
            }
            for (int i = 1; i <= 9; ++i)
            {
                Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] = new SolidColorBrush(
                    new Color((byte)(i * 10 + i), c.R, c.G, c.B)
                );
            }
        }
    }

    private static void InitializeLiveCharts()
    {
        {
            var usingLightTheme = Current?.ActualThemeVariant == ThemeVariant.Light;

            LiveCharts.Configure(
                config =>
                    (usingLightTheme ? config.AddLightTheme() : config.AddDarkTheme())
                    .AddSkiaSharp()
                    .AddDefaultMappers()
            );
        }

        EventService.ThemeConfigChanged += () =>
        {
            var usingLightTheme = Current?.ActualThemeVariant == ThemeVariant.Light;

            LiveCharts.Configure(config =>
            {
                config = usingLightTheme ? config.AddLightTheme() : config.AddDarkTheme();
            });
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var location = $"{nameof(App)}.{nameof(OnFrameworkInitializationCompleted)}";

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        if (ConfigManager.Instance.AppConfig.App.ShowAnnouncementWhenStart)
            new Thread(async () =>
            {
                try
                {
                    await AnouncementManager.CheckNewAnnouncements();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }).Start();

        base.OnFrameworkInitializationCompleted();
    }
}
