using KitX.Shared.Device;
using System.Reflection;
using System;

namespace KitX.Dashboard.Services;

public static class EventService
{
    public delegate void LanguageChangedHandler();

    public static event LanguageChangedHandler LanguageChanged = new(() => { });


    public delegate void GreetingTextIntervalUpdatedHandler();

    public static event GreetingTextIntervalUpdatedHandler GreetingTextIntervalUpdated = new(() => { });


    public delegate void AppConfigChangedHandler();

    public static event AppConfigChangedHandler AppConfigChanged = new(() => { });


    public delegate void PluginsConfigChangedHandler();

    public static event PluginsConfigChangedHandler PluginsConfigChanged = new(() => { });


    public delegate void MicaOpacityChangedHandler();

    public static event MicaOpacityChangedHandler MicaOpacityChanged = new(() => { });


    public delegate void DevelopSettingsChangedHandler();

    public static event DevelopSettingsChangedHandler DevelopSettingsChanged = new(() => { });


    public delegate void LogConfigUpdatedHandler();

    public static event LogConfigUpdatedHandler LogConfigUpdated = new(() => { });


    public delegate void ThemeConfigChangedHandler();

    public static event ThemeConfigChangedHandler ThemeConfigChanged = new(() => { });


    public delegate void UseStatisticsChangedHandler();

    public static event UseStatisticsChangedHandler UseStatisticsChanged = new(() => { });


    public delegate void PluginsServerPortChangedHandler();

    public static event PluginsServerPortChangedHandler PluginsServerPortChanged = new(() => { });


    public delegate void DevicesServerPortChangedHandler();

    public static event DevicesServerPortChangedHandler DevicesServerPortChanged = new(() => { });


    public delegate void OnActivitiesUpdatedHandler();

    public static event OnActivitiesUpdatedHandler OnActivitiesUpdated = new(() => { });


    public delegate void OnExitingHandler();

    public static event OnExitingHandler OnExiting = new(() => { });


    public delegate void OnReceivingDeviceInfoHandler(DeviceInfo dis);

    public static event OnReceivingDeviceInfoHandler OnReceivingDeviceInfo = new(_ => { });


    public delegate void OnConfigHotReloadedHandler();

    public static event OnConfigHotReloadedHandler OnConfigHotReloaded = new(() => { });

    public static void Invoke(string eventName, object[]? objects = null)
    {
        var type = typeof(EventService);

        var eventField = type.GetField(eventName, BindingFlags.Static | BindingFlags.NonPublic);

        if (eventField is null || !typeof(Delegate).IsAssignableFrom(eventField.FieldType))
        {
            throw new ArgumentException($"No event found with the name '{eventName}'.", nameof(eventName));
        }

        var @delegate = eventField.GetValue(null) as Delegate;

        @delegate?.DynamicInvoke(objects);
    }
}
