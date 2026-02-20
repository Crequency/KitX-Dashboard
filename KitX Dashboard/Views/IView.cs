using System;
using KitX.Core.Contract.Event;
using KitX.Core.Event;
using KitX.Dashboard;

namespace KitX.Dashboard.Views;

internal interface IView
{
    internal static void SaveAppConfigChanges()
    {
        var eventService = App.GetService<IEventService>();
        eventService.Publish(EventNames.AppConfigChanged, EventArgs.Empty);
    }
}
