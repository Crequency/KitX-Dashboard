using System;
using Avalonia;
using Avalonia.Controls;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Event;
using ReactiveUI;
using KitX.Core.Event;
using KitX.Core.Configuration;

namespace KitX.Dashboard.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    /// <summary>
    /// Gets the config service from DI container
    /// </summary>
    protected IConfigService ConfigService =>
        App.GetService<IConfigService>();

    /// <summary>
    /// Gets the announcement service from DI container
    /// </summary>
    protected IAnnouncementService AnnouncementService =>
        App.GetService<IAnnouncementService>();

    protected static string? Translate(
        string key = "",
        string prefix = "",
        string suffix = "",
        string separator = "",
        Application? app = null
    )
    {
        app ??= Application.Current;

        if (app is null)
            return null;

        var resKey = $"{prefix}{separator}{key}{separator}{suffix}";

        if (!app.TryFindResource(resKey, out var found))
            return null;

        if (found is string text)
            return text;

        return null;
    }

    protected static string? TranslateText(string key = "", Application? app = null) => Translate(key, "Text", separator: "_", app: app);

    protected static string? TranslateTextWithSuffix(string key = "", string suffix = "", Application? app = null) =>
        Translate(key, "Text", suffix, "_", app);

    /// <summary>
    /// Saves application config changes - Obsolete: Use ConfigService instead
    /// </summary>
    [Obsolete("Use ConfigService.SaveAll() instead.")]
    protected static void SaveAppConfigChanges()
    {
        var configService = App.GetService<IConfigService>();
        configService.SaveAll();
        var eventService = App.GetService<IEventService>();
        eventService.Publish(EventNames.AppConfigChanged, EventArgs.Empty);
    }

    /// <summary>
    /// Gets application config - Obsolete: Use ConfigService.AppConfig instead
    /// </summary>
    [Obsolete("Use ConfigService.AppConfig instead.")]
    internal static AppConfig AppConfig
    {
        get
        {
            var configService = App.GetService<IConfigService>();
            return configService.AppConfig as AppConfig ?? new();
        }
    }

    /// <summary>
    /// Gets announcement config - Obsolete: Use AnnouncementService.AnnouncementConfig instead
    /// </summary>
    [Obsolete("Use AnnouncementService.AnnouncementConfig instead.")]
    internal static AnnouncementConfig AnnouncementConfig
    {
        get
        {
            // Use IAnnouncementService.AnnouncementConfig for strong type access
            var announcementService = App.GetService<IAnnouncementService>();
            return announcementService.AnnouncementConfig as AnnouncementConfig
                ?? new();
        }
    }

    public abstract void InitCommands();

    public abstract void InitEvents();
}
