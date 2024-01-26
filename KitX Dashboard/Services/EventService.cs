using KitX.Web.Rules;
using KitX.Web.Rules.Plugin;
using KitX.Web.Rules.Device;

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

    internal delegate void OnReceivingDeviceInfoHandler(DeviceInfo dis);

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

    internal static event OnReceivingDeviceInfoHandler? OnReceivingDeviceInfo;

    internal static event OnConfigHotReloadedHandler? OnConfigHotReloaded;

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
        OnReceivingDeviceInfo += dis => { };
        OnConfigHotReloaded += () => { };
        PluginsServerPortChanged += () => { };
    }

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

    internal static void Invoke(string eventName, object arg)
    {
        switch (eventName)
        {
            case nameof(OnReceivingDeviceInfo):
                OnReceivingDeviceInfo?.Invoke((DeviceInfo)arg);
                break;
        }
    }
}
