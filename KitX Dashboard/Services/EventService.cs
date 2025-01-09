using System;
using System.Reflection;
using KitX.Shared.CSharp.Device;

namespace KitX.Dashboard.Services;

public static class EventService
{
    public static void Invoke(string eventName, object[]? objects = null)
    {
        var type = typeof(EventService);

        var eventField = type.GetField(eventName, BindingFlags.Static | BindingFlags.NonPublic);

        if (eventField is null || !typeof(Delegate).IsAssignableFrom(eventField.FieldType))
        {
            throw new ArgumentException(
                $"No event found with the name '{eventName}'.",
                nameof(eventName)
            );
        }

        var @delegate = eventField.GetValue(null) as Delegate;

        @delegate?.DynamicInvoke(objects);
    }

#pragma warning disable CS0067 // Event is never used

    public delegate void LanguageChangedHandler();

    public static event LanguageChangedHandler LanguageChanged = () => { };

    public delegate void GreetingTextIntervalUpdatedHandler();

    public static event GreetingTextIntervalUpdatedHandler GreetingTextIntervalUpdated = () => { };

    public delegate void AppConfigChangedHandler();

    public static event AppConfigChangedHandler AppConfigChanged = () => { };

    public delegate void PluginsConfigChangedHandler();

    public static event PluginsConfigChangedHandler PluginsConfigChanged = () => { };

    public delegate void MicaOpacityChangedHandler();

    public static event MicaOpacityChangedHandler MicaOpacityChanged = () => { };

    public delegate void DevelopSettingsChangedHandler();

    public static event DevelopSettingsChangedHandler DevelopSettingsChanged = () => { };

    public delegate void LogConfigUpdatedHandler();

    public static event LogConfigUpdatedHandler LogConfigUpdated = () => { };

    public delegate void ThemeConfigChangedHandler();

    public static event ThemeConfigChangedHandler ThemeConfigChanged = () => { };

    public delegate void UseStatisticsChangedHandler();

    public static event UseStatisticsChangedHandler UseStatisticsChanged = () => { };

    public delegate void DevicesServerPortChangedHandler(int port);

    public static event DevicesServerPortChangedHandler DevicesServerPortChanged = port =>
        ConstantTable.DevicesServerPort = port;

    public delegate void PluginsServerPortChangedHandler(int port);

    public static event PluginsServerPortChangedHandler PluginsServerPortChanged = port =>
        ConstantTable.PluginsServerPort = port;

    public delegate void OnActivitiesUpdatedHandler();

    public static event OnActivitiesUpdatedHandler OnActivitiesUpdated = () => { };

    public delegate void OnReceiveCancelExchangingDeviceKeyHandler();

    public static event OnReceiveCancelExchangingDeviceKeyHandler OnReceiveCancelExchangingDeviceKey =
        () => ConstantTable.IsExchangingDeviceKey = false;

    public delegate void OnExitingHandler();

    public static event OnExitingHandler OnExiting = () => { };

    public delegate void OnReceivingDeviceInfoHandler(DeviceInfo dis);

    public static event OnReceivingDeviceInfoHandler OnReceivingDeviceInfo = _ => { };

    public delegate void OnConfigHotReloadedHandler();

    public static event OnConfigHotReloadedHandler OnConfigHotReloaded = () => { };

    public delegate void OnAcceptingDeviceKeyHandler(string code);

    public static event OnAcceptingDeviceKeyHandler OnAcceptingDeviceKey = _ => { };

#pragma warning restore CS0067 // Event is never used
}
