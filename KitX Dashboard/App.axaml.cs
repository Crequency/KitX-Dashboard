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
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.DI;
using KitX.Dashboard.Services;
using KitX.Dashboard.Utils;
using KitX.WorkflowV6.Hosting;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.ViewModels.Maintain;
using KitX.Dashboard.ViewModels.Pages;
using KitX.Dashboard.ViewModels.Pages.Controls;
using KitX.Dashboard.Views;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

using ServiceHost = KitX.Core.DI.ServiceHost;

namespace KitX.Dashboard;

public partial class App : Application
{
    /// <summary>
    /// Initialize DI container before UI framework starts
    /// This should be called from AppFramework.RunFramework() before any UI code runs
    /// Note: This is now called BEFORE Logger initialization (Phase 2 refactoring).
    /// Log.Debug() calls in service constructors (e.g., ConfigManager) are no-ops
    /// until Serilog Logger is configured later in RunFramework().
    /// </summary>
    internal static void InitializeServiceProvider()
    {
        if (ServiceHost.IsInitialized)
            return;

        Log.Information("Initializing service provider...");

        // Initialize service provider with Core services
        var services = new ServiceCollection();

        // Register Core services from KitX.Core
        services.AddCoreServices();

        // V6 workflow services — the only workflow backend since v5.1 was archived.
        // Shared interface registrations (ILens<>, IExecutionBackend) now resolve to V6.
        services.AddKitXWorkflowV6();

        // Register Dashboard-specific services
        services.AddSingleton<IFileDialogService, FileDialogService>();

        // Device key-exchange UI + receive-side coordinator. Subscribes to the receive
        // exchange event in its constructor, so it must be eagerly resolved after the
        // provider is built (below).
        services.AddSingleton<IDeviceKeyExchangeUi, DeviceKeyExchangeUiService>();

        // S2/S4/S6 (WorkflowStorageService / WorkflowSessionManager / TriggerManager) are
        // now registered inside AddKitXWorkflowV6() above (migrated from Dashboard to
        // KitX.WorkflowV6.Services).

        // Register SignalTasksManager for signal-based coordination
        services.AddSingleton<Common.BasicHelper.Core.TaskSystem.SignalTasksManager>();

        // Register Dashboard ViewModels (for DI auto-resolution without ActivatorUtilities fallback)
        // S5: WorkflowScriptEditorWindowViewModel retired — functionality merged into WorkflowEditorViewModel.
        services.AddTransient<DebugWindowViewModel>();
        services.AddTransient<Settings_GeneralViewModel>();
        services.AddTransient<Settings_PerformanceViewModel>();

        // S0: page/window ViewModels resolved via App.GetService at their View creation points
        // (Avalonia constructs Views directly — no container injection into View constructors).
        services.AddTransient<WorkflowEditorViewModelV6>();
        services.AddTransient<PluginsLaunchWindowViewModel>();
        services.AddTransient<WorkflowPageViewModel>();
        services.AddTransient<DevicesPageViewModel>();

        // C3 convergence: all remaining ViewModels — constructor-injected services,
        // resolved via App.GetService at their View creation points.
        services.AddTransient<AppViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DebugOptionsWindowViewModel>();
        services.AddTransient<AnnouncementsWindowViewModel>();
        services.AddTransient<ExchangeDeviceKeyWindowViewModel>();
        services.AddTransient<PluginDetailWindowViewModel>();
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<LibPageViewModel>();
        services.AddTransient<RepoPageViewModel>();
        services.AddTransient<MarketPageViewModel>();
        services.AddTransient<AccountPageViewModel>();
        services.AddTransient<DevelopingViewModel>();
        services.AddTransient<Home_ActivityLogViewModel>();
        services.AddTransient<Home_CountViewModel>();
        services.AddTransient<Home_RecentUseViewModel>();
        services.AddTransient<PluginBarViewModel>();
        services.AddTransient<Settings_AboutViewModel>();
        services.AddTransient<Settings_PersonaliseViewModel>();
        services.AddTransient<Settings_UpdateViewModel>();
        services.AddTransient<SettingsPageViewModel>();

        // Build the SINGLE IServiceProvider — no duplicate BuildServiceProvider calls
        var provider = services.BuildServiceProvider();

        // Initialize ServiceHost with the single provider (centralized service access)
        ServiceHost.Initialize(provider);

        // Eagerly resolve the key-exchange UI service so its receive-side event
        // subscription is active even before the Devices page is opened.
        _ = provider.GetRequiredService<IDeviceKeyExchangeUi>();

        // Initialize the workflow library's own service locator with the same provider,
        // so workflow code created outside DI (builtin functions, lazy singletons) can
        // resolve shared services (IPluginService, IDeviceServer, workflow services, ...).
        //
        // Phase 12-prep: legacy KitX.Workflow.Hosting.ServiceLocator archived; v6 runs
        // fully through the DI container. Workflow eager-resolution is intentionally disabled.
        // KitX.Workflow.Hosting.ServiceLocator.Initialize(provider);

        // Pre-resolve the plugin manager bridge to force eager singleton construction
        // (the concrete RealPluginManager subscribes to plugin events in its ctor).
        // Phase 12-prep: bridge no longer registered (old lib archived). Re-enable when
        // the new library's RealPluginManager equivalent is wired.
        // var rpm = provider.GetRequiredService<IRealPluginManagerBridge>();
        // Log.Information("RealPluginManager pre-resolved. HashCode: {HashCode}", rpm.GetHashCode());

        // NOTE: no startup trigger re-subscription — a workflow is armed ONLY while the
        // user keeps it Running (Run=register, Stop=unregister). The old
        // InitializeFromPersistedWorkflows silently armed every saved PluginEvent
        // workflow at launch without touching the card's mounted indicator (and without
        // checking plugin connectivity), desyncing the UI from actual trigger routing.

        Log.Information("Service provider initialized.");
    }

    /// <summary>
    /// Gets service from DI container.
    /// Throws if the service is not registered — all types must be explicitly registered.
    /// </summary>
    public static T GetService<T>() where T : class
    {
        Log.Debug($"Getting service: {typeof(T).Name}");

        if (!ServiceHost.IsInitialized)
        {
            Log.Warning("ServiceHost not initialized, initializing now (this should not happen in normal flow)...");
            InitializeServiceProvider();
        }

        var service = ServiceHost.ServiceProvider.GetService(typeof(T));
        if (service != null)
            return (T)service;

        // Service not registered — throw to make missing registrations visible at runtime
        throw new InvalidOperationException(
            $"Service '{typeof(T).Name}' is not registered in the DI container. " +
            "Ensure it is added via services.AddSingleton/AddTransient/AddScoped in InitializeServiceProvider()."
        );
    }

    /// <summary>
    /// Cached default icon (D4). Previously re-decoded from disk on every access.
    /// Owned by this static cache for process lifetime — one small bitmap, bounded.
    /// </summary>
    private static Bitmap? _defaultIconCache;

    private static string? _defaultIconCacheKey;

    public static Bitmap? DefaultIcon
    {
        get
        {
            var configService = GetService<IConfigService>();
            var path = Path.Combine(ConstantTable.AssetsPath, configService.AppConfig.App.CoverIconFileName).GetFullPath();

            if (Design.IsDesignMode)
                return null;

            if (_defaultIconCacheKey == path && _defaultIconCache is not null)
                return _defaultIconCache;

            _defaultIconCache = new(path);
            _defaultIconCacheKey = path;

            return _defaultIconCache;
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
        viewModel = GetService<AppViewModel>();

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

    private static void LoadLanguage() => LanguageLoader.LoadLanguage();

    private static void CalculateThemeColor()
    {
        var configService = GetService<IConfigService>();
        Color c = Color.Parse(configService.AppConfig.App.ThemeColor);

        if (Current is not null)
            ThemeColorPalette.ApplyTo(Current, c);
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
            desktop.MainWindow = new MainWindow { DataContext = GetService<MainWindowViewModel>() };
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
