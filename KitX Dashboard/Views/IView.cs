using KitX.Dashboard.Services;

namespace KitX.Dashboard.Views;

internal interface IView
{
    internal static void SaveAppConfigChanges()
    {
        EventService.Invoke(nameof(EventService.AppConfigChanged));
    }
}
