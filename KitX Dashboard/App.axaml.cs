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
using KitX.Core.Announcement;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.DI;
using KitX.Core.Event;
using KitX.Core.Contract.Event;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.ViewModels.Pages.Controls;
using KitX.Dashboard.Views;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KitX.Dashboard;

public partial class App : Application
{
    /// <summary>
    /// Service provider for dependency injection
    /// </summary>
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Initialize DI container before UI framework starts
    /// This should be called from AppFramework.RunFramework() before any UI code runs
    /// Logger is guaranteed to be initialized before this is called
    /// </summary>
    internal static void InitializeServiceProvider()
    {
        if (_serviceProvider != null)
            return;

        Log.Information("Initializing service provider...");

        // Initialize service provider with Core services
        var services = new ServiceCollection();

        // Register Core services from KitX.Core
        // ViewModels with constructor dependencies will be auto-resolved via ActivatorUtilities
        services.AddCoreServices();

        _serviceProvider = services.BuildServiceProvider();

        Log.Information("Service provider initialized.");
    }

    /// <summary>
    /// Gets service from DI container
    /// Supports automatic resolution of unregistered types via constructor injection
    /// Logger is guaranteed to be initialized when this is called
    /// </summary>
    public static T GetService<T>() where T : class
    {
        Log.Information($"Getting service: {typeof(T).Name}");

        if (_serviceProvider == null)
        {
            Log.Warning("Service provider is null, initializing now (this should not happen in normal flow)...");

            InitializeServiceProvider();
        }

        try
        {
            var result = _serviceProvider!.GetRequiredService<T>();

            Log.Information($"Got service: {typeof(T).Name}");

            return result;
        }
        catch (InvalidOperationException)
        {
            // Type not registered - try auto-resolution via ActivatorUtilities
            // This allows constructor injection without explicit registration
            Log.Information($"Auto-resolving unregistered service: {typeof(T).Name}");

            var result = ActivatorUtilities.CreateInstance<T>(_serviceProvider!);

            Log.Information($"Auto-resolved service: {typeof(T).Name}");

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to get service: {typeof(T).Name}");
            throw;
        }
    }

    public static Bitmap? DefaultIcon
    {
        get
        {
            var configService = GetService<IConfigService>();
            var path = Path.Combine(ConstantTable.AssetsPath, configService.AppConfig.App.CoverIconFileName).GetFullPath();

            if (Design.IsDesignMode)
                return null;

            return new(path);
        }
    }

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
        var configService = GetService<IConfigService>();
        RequestedThemeVariant = configService.AppConfig.App.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            "Follow" => ThemeVariant.Default,
            _ => ThemeVariant.Default,
        };
    }

    private void LoadLanguage()
    {
        var configService = GetService<IConfigService>();
        var config = configService.AppConfig;
        var lang = config.App.AppLanguage;
        var backup_lang = config.App.SurpportLanguages.Keys.First();
        var path = $"{ConstantTable.LanguageFilePath}/{lang}.axaml".GetFullPath();
        var backup_langPath = $"{ConstantTable.LanguageFilePath}/{backup_lang}.axaml".GetFullPath();

        try
        {
            Resources.MergedDictionaries.Clear();

            Resources.MergedDictionaries.Add(AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)) as ResourceDictionary ?? []);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Language File {lang}.axaml not found.");

            Resources.MergedDictionaries.Clear();

            try
            {
                Resources.MergedDictionaries.Add(
                    AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(backup_langPath)) as ResourceDictionary ?? []
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
            var eventService = GetService<IEventService>();
            eventService.Publish(EventNames.LanguageChanged, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Log.Warning(e, $"Failed to invoke language changed event.");
        }
    }

    private static void CalculateThemeColor()
    {
        var configService = GetService<IConfigService>();
        Color c = Color.Parse(configService.AppConfig.App.ThemeColor);

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

            LiveCharts.Configure(config =>
                (usingLightTheme ? config.AddLightTheme() : config.AddDarkTheme()).AddSkiaSharp().AddDefaultMappers()
            );
        }

        var eventService = GetService<IEventService>();
        eventService.Subscribe(EventNames.ThemeConfigChanged, (s, e) =>
        {
            var usingLightTheme = Current?.ActualThemeVariant == ThemeVariant.Light;

            LiveCharts.Configure(config =>
            {
                config = usingLightTheme ? config.AddLightTheme() : config.AddDarkTheme();
            });
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        const string location = $"{nameof(App)}.{nameof(OnFrameworkInitializationCompleted)}";

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
        }

        var configService = GetService<IConfigService>();
        if (configService.AppConfig.App.ShowAnnouncementWhenStart)
        {
            var announcementService = GetService<IAnnouncementService>();
            new Thread(async () => await announcementService.CheckNewAnnouncementsAsync()).Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
