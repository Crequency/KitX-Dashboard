using System;
using Avalonia;
using Avalonia.Controls;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Event;
using ReactiveUI;
using KitX.Core.Configuration;

namespace KitX.Dashboard.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    // NOTE (ServiceLocator convergence): these static accessors are RETAINED only for
    // Settings_UpdateViewModel, whose body (Update() / busy-wait / Components logic) is
    // frozen during the server-side refactor and may not be touched. Every other VM has
    // been migrated to constructor injection and no longer uses them.
    /// <summary>
    /// Gets the config service from DI container
    /// </summary>
    protected static IConfigService ConfigService =>
        App.GetService<IConfigService>();

    /// <summary>
    /// Gets the announcement service from DI container
    /// </summary>
    protected static IAnnouncementService AnnouncementService =>
        App.GetService<IAnnouncementService>();

    public static string? Translate(
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

    public static string? TranslateText(string key = "", Application? app = null) => Translate(key, "Text", separator: "_", app: app);

    public static string? TranslateTextWithSuffix(string key = "", string suffix = "", Application? app = null) =>
        Translate(key, "Text", suffix, "_", app);

    public abstract void InitCommands();

    public abstract void InitEvents();
}
