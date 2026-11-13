using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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

        // Looking a resource up in an Application is a UI-thread operation in everything but its
        // signature: the lookup reaches Application.Styles, which is built lazily and whose
        // construction calls AvaloniaObject's constructor, and that one calls
        // Dispatcher.VerifyAccess. A caller on another thread therefore gets an answer, a miss or a
        // "Call from invalid thread" exception depending on whether the application has finished
        // initialising -- which is a race, not a rule.
        //
        // Measured, not inferred: seventeen Bench tests failed at once with exactly that exception
        // from Avalonia.Threading.Dispatcher.VerifyAccess <- AvaloniaObject..ctor <- Styles..ctor <-
        // Application.get_Styles() <- Application.TryGetResource <- this method, in one full-suite
        // run, while the next two runs were clean and the same classes passed when run alone.
        //
        // Returning null is well defined here because every call site is written as
        // TranslateText(key) ?? "fallback": the fallback is the answer the caller already handles.
        if (!Dispatcher.UIThread.CheckAccess())
            return null;

        // Each segment carries its own separator, so an empty one contributes nothing. Joining
        // all three unconditionally left a trailing underscore whenever suffix was empty, and
        // every key in Languages/*.axaml is written without one: TranslateText answered null for
        // every key it was ever given, and the `?? "English fallback"` beside each call site was
        // the only text anybody ever saw. Measured on zh-cn.axaml: TranslateText
        // ("Bench_Surface_RowOutOfRange") returned null, while the resource
        // Text_Bench_Surface_RowOutOfRange resolved to "行号至少为 1。".
        var resKey = string.Concat(
            string.IsNullOrEmpty(prefix) ? string.Empty : prefix + separator,
            key,
            string.IsNullOrEmpty(suffix) ? string.Empty : separator + suffix);

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
