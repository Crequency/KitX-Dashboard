namespace KitX.Dashboard.Configuration;

public class ConfigFetcher
{
    public static AppConfig AppConfig => Instances.ConfigManager.AppConfig;
}
