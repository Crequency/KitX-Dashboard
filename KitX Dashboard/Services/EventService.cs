using KitX.Web.Rules;

namespace KitX.Dashboard.Services;

internal static class EventService
{

    internal delegate void LanguageChangedHandler();

    internal delegate void GreetingTextIntervalUpdatedHandler();

    internal delegate void ConfigSettingsChangedHandler();

    internal delegate void MicaOpacityChangedHandler();

    internal delegate void PluginsListChangedHandler();

    internal delegate void DevelopSettingsChangedHandler();

    internal delegate void LogConfigUpdatedHandler();

    internal delegate void ThemeConfigChangedHandler();

    internal delegate void UseStatisticsChangedHandler();

    internal delegate void OnExitingHandler();

    internal delegate void PluginsServerPortChangedHandler();

    internal delegate void DevicesServerPortChangedHandler();

    internal delegate void OnReceivingDeviceInfoStructHandler(DeviceInfoStruct dis);

    internal delegate void OnConfigHotReloadedHandler();



    internal static event LanguageChangedHandler? LanguageChanged;

    internal static event GreetingTextIntervalUpdatedHandler? GreetingTextIntervalUpdated;

    internal static event ConfigSettingsChangedHandler? ConfigSettingsChanged;

    internal static event MicaOpacityChangedHandler? MicaOpacityChanged;

    internal static event PluginsListChangedHandler? PluginsListChanged;

    internal static event DevelopSettingsChangedHandler? DevelopSettingsChanged;

    internal static event LogConfigUpdatedHandler? LogConfigUpdated;

    internal static event ThemeConfigChangedHandler? ThemeConfigChanged;

    internal static event UseStatisticsChangedHandler? UseStatisticsChanged;

    internal static event OnExitingHandler? OnExiting;

    internal static event PluginsServerPortChangedHandler? PluginsServerPortChanged;

    internal static event DevicesServerPortChangedHandler? DevicesServerPortChanged;

    internal static event OnReceivingDeviceInfoStructHandler? OnReceivingDeviceInfoStruct;

    internal static event OnConfigHotReloadedHandler? OnConfigHotReloaded;


    /// <summary>
    /// 必要的初始化
    /// </summary>
    internal static void Init()
    {
        LanguageChanged += () => { };
        GreetingTextIntervalUpdated += () => { };
        ConfigSettingsChanged += () => { };
        MicaOpacityChanged += () => { };
        PluginsListChanged += () => { };
        DevelopSettingsChanged += () => { };
        LogConfigUpdated += () => { };
        ThemeConfigChanged += () => { };
        UseStatisticsChanged += () => { };
        OnExiting += () => { };
        DevicesServerPortChanged += () => { };
        OnReceivingDeviceInfoStruct += dis => { };
        OnConfigHotReloaded += () => { };
        PluginsServerPortChanged += () => { };
    }

    /// <summary>
    /// 执行全局事件
    /// </summary>
    /// <param name="eventName">事件名称</param>
    internal static void Invoke(string eventName)
    {
        switch (eventName)
        {
            case nameof(LanguageChanged):
                LanguageChanged?.Invoke();
                break;
            case nameof(GreetingTextIntervalUpdated):
                GreetingTextIntervalUpdated?.Invoke();
                break;
            case nameof(ConfigSettingsChanged):
                ConfigSettingsChanged?.Invoke();
                break;
            case nameof(MicaOpacityChanged):
                MicaOpacityChanged?.Invoke();
                break;
            case nameof(PluginsListChanged):
                PluginsListChanged?.Invoke();
                break;
            case nameof(DevelopSettingsChanged):
                DevelopSettingsChanged?.Invoke();
                break;
            case nameof(LogConfigUpdated):
                LogConfigUpdated?.Invoke();
                break;
            case nameof(ThemeConfigChanged):
                ThemeConfigChanged?.Invoke();
                break;
            case nameof(UseStatisticsChanged):
                UseStatisticsChanged?.Invoke();
                break;
            case nameof(OnExiting):
                OnExiting?.Invoke();
                break;
            case nameof(DevicesServerPortChanged):
                DevicesServerPortChanged?.Invoke();
                break;
            case nameof(OnConfigHotReloaded):
                OnConfigHotReloaded?.Invoke();
                break;
            case nameof(PluginsServerPortChanged):
                PluginsServerPortChanged?.Invoke();
                break;
        }
    }

    /// <summary>
    /// 执行全局事件
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <param name="arg">事件参数</param>
    internal static void Invoke(string eventName, object arg)
    {
        switch (eventName)
        {
            case nameof(OnReceivingDeviceInfoStruct):
                OnReceivingDeviceInfoStruct?.Invoke((DeviceInfoStruct)arg);
                break;
        }
    }
}
