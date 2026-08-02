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
using KitX.Core.Contract.Workflow;
using KitX.Core.DI;
using KitX.Core.Event;
using KitX.Dashboard.Services;
using KitX.Workflow.Hosting;
using KitX.WorkflowV6.Hosting;
using KitX.Dashboard.ViewModels;
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

        // V6 workflow services. Registered *before* V5 so that the V6 concrete types
        // (KsTextLens, BpGraphLens, SyncService, IScopeAnalyzer) are resolvable for the
        // v6 editor, while the shared interface registrations (ILens<>, IExecutionBackend)
        // remain pointed at V5 implementations — the v5 editor is still the active one.
        services.AddKitXWorkflowV6();

        // Phase F1: register the WorkflowIR (v5) library services (IR/Lens/Diff/Backend/Session).
        // Registered after V6 so shared interfaces (ILens<>, IExecutionBackend) resolve to V5.
        // This replaces the archived AddKitXWorkflow() entry (CoreServiceCollectionExtensions.cs:117).
        services.AddKitXWorkflowIR();

        // NodeFactory: replaces the orphaned INodeRegistry (uses BuiltinFunctionRegistry).
        services.AddSingleton<KitX.Dashboard.Services.NodeFactory>(sp =>
            new KitX.Dashboard.Services.NodeFactory(
                sp.GetRequiredService<KitX.Workflow.Builtin.BuiltinFunctionRegistry>()));

        // §2.3 fix: bridge the Dashboard's plugin services to Kscript's IPluginServiceProvider,
        // then register RealPluginManager as the live IPluginManager. This replaces the
        // NoOpPluginManager fallback so workflow PluginCall(...) builtins reach live plugins.
        services.AddSingleton<Kscript.CSharp.Parser.Core.IPluginServiceProvider>(sp =>
            new KitX.Dashboard.Services.DashboardPluginServiceProvider(
                sp.GetRequiredService<KitX.Core.Contract.Plugin.IPluginServer>(),
                sp.GetRequiredService<KitX.Core.Contract.Event.IEventService>()));
        services.AddSingleton<Kscript.CSharp.Parser.Core.IPluginManager>(sp =>
            new Kscript.CSharp.Parser.Core.RealPluginManager(
                sp.GetRequiredService<Kscript.CSharp.Parser.Core.IPluginServiceProvider>()));

        // IPluginHost adapter: wraps RealPluginManager for workflow PluginCall execution.
        // Resolves the live IPluginManager (registered above); NoOpPluginManager remains as a
        // defensive fallback if the registration is ever removed.
        services.AddSingleton<KitX.Workflow.Backend.Runtime.IPluginHost>(sp =>
        {
            return new KitX.Dashboard.Services.PluginHostAdapter(
                sp.GetService<Kscript.CSharp.Parser.Core.IPluginManager>()
                    ?? new NoOpPluginManager());
        });

        // The v6 workflow runtime uses the same adapter (identical interface contract).
        // Without this registration StructuredRoslynBackend's optional IPluginHost
        // parameter resolves to null and v6 PluginCall silently returns null.
        services.AddSingleton<KitX.WorkflowV6.Backend.Runtime.IPluginHost>(sp =>
            (KitX.WorkflowV6.Backend.Runtime.IPluginHost)
                sp.GetRequiredService<KitX.Workflow.Backend.Runtime.IPluginHost>());

        // Register Dashboard-specific services
        services.AddSingleton<IFileDialogService, FileDialogService>();

        // S2: WorkflowStorageService — file-based IWorkflowStorageService for KcsFileFormat v2 (IR as storage).
        services.AddSingleton<KitX.Core.Contract.Workflow.IWorkflowStorageService,
            KitX.Dashboard.Services.WorkflowStorageService>();

        // S4: WorkflowSessionManager — IWorkflowManagementService orchestrator (run/stop by id
        // via stored IR + IExecutionBackend). Replaces the archived WorkflowManagementService.
        // Dispatches both v5 (WorkflowIR) and v6 (WorkflowV6) .kcs formats (P3-δ).
        services.AddSingleton<KitX.Core.Contract.Workflow.IWorkflowManagementService,
            KitX.Dashboard.Services.WorkflowSessionManager>();

        // S6: TriggerManager — ITriggerManager implementation (rebuilt from the archived
        // ServiceLocator-based version with constructor injection). Routes plugin
        // TriggerFired signals to subscribed workflows (P3-δ).
        services.AddSingleton<KitX.Core.Contract.Workflow.ITriggerManager,
            KitX.Dashboard.Services.TriggerManager>();

        // Register SignalTasksManager for signal-based coordination
        services.AddSingleton<Common.BasicHelper.Core.TaskSystem.SignalTasksManager>();

        // Register Dashboard ViewModels (for DI auto-resolution without ActivatorUtilities fallback)
        // S5: WorkflowScriptEditorWindowViewModel retired — functionality merged into WorkflowEditorViewModel.
        services.AddTransient<DebugWindowViewModel>();
        services.AddTransient<BlueprintEditorViewModel>();
        services.AddTransient<Settings_GeneralViewModel>();
        services.AddTransient<Settings_PerformenceViewModel>();

        // Build the SINGLE IServiceProvider — no duplicate BuildServiceProvider calls
        var provider = services.BuildServiceProvider();

        // Initialize ServiceHost with the single provider (centralized service access)
        ServiceHost.Initialize(provider);

        // Initialize the workflow library's own service locator with the same provider,
        // so workflow code created outside DI (builtin functions, lazy singletons) can
        // resolve shared services (IPluginService, IDeviceServer, workflow services, ...).
        //
        // Phase 12-prep: legacy KitX.Workflow.Hosting.ServiceLocator archived; the new
        // KitX.WorkflowIR library will expose its own service-locator / DI entry once the
        // editor migrates. Workflow eager-resolution is intentionally disabled for now.
        // KitX.Workflow.Hosting.ServiceLocator.Initialize(provider);

        // Pre-resolve the plugin manager bridge to force eager singleton construction
        // (the concrete RealPluginManager subscribes to plugin events in its ctor).
        // Phase 12-prep: bridge no longer registered (old lib archived). Re-enable when
        // the new library's RealPluginManager equivalent is wired.
        // var rpm = provider.GetRequiredService<IRealPluginManagerBridge>();
        // Log.Information("RealPluginManager pre-resolved. HashCode: {HashCode}", rpm.GetHashCode());

        // Initialize TriggerManager from persisted workflow configurations (P3-δ).
        // Runs before plugins connect so early TriggerFired events still route correctly.
        var triggerManager = provider.GetRequiredService<KitX.Core.Contract.Workflow.ITriggerManager>();
        triggerManager.InitializeFromPersistedWorkflows();

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
