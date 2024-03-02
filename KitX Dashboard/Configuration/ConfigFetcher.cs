using KitX.Dashboard.Managers;

namespace KitX.Dashboard.Configuration;

public class ConfigFetcher
{
    public static AppConfig AppConfig => ConfigManager.Instance.AppConfig;

    public static AnnouncementConfig AnnouncementConfig => ConfigManager.Instance.AnnouncementConfig;

    public static MarketConfig MarketConfig => ConfigManager.Instance.MarketConfig;

    public static PluginsConfig PluginsConfig => ConfigManager.Instance.PluginsConfig;

    public static SecurityConfig SecurityConfig => ConfigManager.Instance.SecurityConfig;
}
