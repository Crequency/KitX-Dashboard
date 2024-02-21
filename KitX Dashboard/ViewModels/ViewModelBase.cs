using Avalonia;
using Avalonia.Controls;
using KitX.Dashboard;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Services;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    protected static string? FetchStringFromResource(
        Application? app,
        string key,
        string prefix = "",
        string suffix = "",
        string seperator = "")
    {
        if (app is null) return null;

        var res_key = $"{prefix}{seperator}{key}{seperator}{suffix}";

        if (app.TryFindResource(res_key, out var found))
        {
            if (found is string text) return text;
            else return null;
        }
        else return null;
    }

    protected static void SaveAppConfigChanges() => EventService.Invoke(
        nameof(EventService.AppConfigChanged)
    );

    public abstract void InitCommands();

    public abstract void InitEvents();

    internal static AppConfig AppConfig => Instances.ConfigManager.AppConfig;
}
