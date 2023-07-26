using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Common.BasicHelper.IO;
using Common.BasicHelper.Utils.Extensions;
using FluentAvalonia.Styling;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using KitX_Dashboard.ViewModels;
using KitX_Dashboard.Views;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Serilog;
using System;
using System.Linq;
using System.Threading;

namespace KitX_Dashboard;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        LoadLanguage();

        CalculateThemeColor();

        InitLiveCharts();
    }

    /// <summary>
    /// 加载语言
    /// </summary>
    private void LoadLanguage()
    {
        var lang = ConfigManager.AppConfig.App.AppLanguage;
        var backup_lang = ConfigManager.AppConfig.App.SurpportLanguages.Keys.First();
        var path = $"{GlobalInfo.LanguageFilePath}/{lang}.axaml".GetFullPath();
        var backup_langPath = $"{GlobalInfo.LanguageFilePath}/{backup_lang}.axaml";

        backup_langPath = backup_langPath.GetFullPath();

        try
        {
            Resources.MergedDictionaries.Clear();

            Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    FileHelper.ReadAll(path)
                ) as ResourceDictionary ?? new()
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
                        FileHelper.ReadAll(backup_langPath)
                    ) as ResourceDictionary ?? new()
                );
                ConfigManager.AppConfig.App.AppLanguage = backup_lang;
            }
            catch (Exception e)
            {
                Log.Warning(e, $"Suspected absence of language files on record.");
            }
        }
        finally
        {
            Log.Warning($"No surpport language file loaded.");
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

    /// <summary>
    /// 计算主题色
    /// </summary>
    private static void CalculateThemeColor()
    {
        Color c = Color.Parse(ConfigManager.AppConfig.App.ThemeColor);

        if (Current is not null)
        {
            Current.Resources["ThemePrimaryAccent"] =
                new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));
            for (char i = 'A'; i <= 'E'; ++i)
            {
                Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] =
                    new SolidColorBrush(new Color((byte)(170 + (i - 'A') * 17), c.R, c.G, c.B));
            }
            for (int i = 1; i <= 9; ++i)
            {
                Current.Resources[$"ThemePrimaryAccentTransparent{i}{i}"] =
                    new SolidColorBrush(new Color((byte)(i * 10 + i), c.R, c.G, c.B));
            }
        }
    }

    /// <summary>
    /// 初始化图表系统
    /// </summary>
    private static void InitLiveCharts()
    {
        LiveCharts.Configure(config =>
        {
            config
                .AddSkiaSharp()
                .AddDefaultMappers();

            switch (AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>()?.RequestedTheme)
            {
                case "Light": config.AddLightTheme(); break;
                case "Dark": config.AddDarkTheme(); break;
                default: config.AddLightTheme(); break;
            };
        });

        EventService.ThemeConfigChanged += () =>
        {
            switch (AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>()?.RequestedTheme)
            {
                case "Light":
                    LiveCharts.Configure(config => config.AddLightTheme());
                    break;
                case "Dark":
                    LiveCharts.Configure(config => config.AddDarkTheme());
                    break;
                default:
                    LiveCharts.Configure(config => config.AddLightTheme());
                    break;
            };
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        if (ConfigManager.AppConfig.App.ShowAnnouncementWhenStart)
            new Thread(async () =>
            {
                try
                {
                    await AnouncementManager.CheckNewAnnouncements();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "In AnouncementManager.CheckNewAccnouncements()");
                }
            }).Start();

        base.OnFrameworkInitializationCompleted();
    }

    public static readonly Bitmap DefaultIcon = new(
        $"{GlobalInfo.AssetsPath}{ConfigManager.AppConfig.App.CoverIconFileName}".GetFullPath()
    );
}
