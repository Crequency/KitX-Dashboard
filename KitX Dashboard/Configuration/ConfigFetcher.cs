using KitX.Dashboard.Managers;

namespace KitX.Dashboard.Configuration;

public class ConfigFetcher
{
    public static AppConfig AppConfig => ConfigManager.Instance.AppConfig;
}
