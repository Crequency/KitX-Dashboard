using System;
using KitX.Core.Contract.Event;
using KitX.Dashboard;

namespace KitX.Dashboard.Utils;

/// <summary>
/// Event bus helper for simplified event publishing and subscribing
/// </summary>
public static class Events
{
    private static IEventService? _eventService;

    /// <summary>
    /// Gets the event service instance
    /// </summary>
    public static IEventService Service => _eventService ??= App.GetService<IEventService>();

    /// <summary>
    /// Publishes an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="args">The event arguments</param>
    public static void Publish(string eventName, EventArgs args = null!)
        => Service.Publish(eventName, args ?? EventArgs.Empty);

    /// <summary>
    /// Publishes a typed event
    /// </summary>
    /// <typeparam name="TEventArgs">The event args type</typeparam>
    /// <param name="eventName">The event name</param>
    /// <param name="args">The event arguments</param>
    public static void Publish<TEventArgs>(string eventName, TEventArgs args)
        where TEventArgs : EventArgs
        => Service.Publish(eventName, args);

    /// <summary>
    /// Subscribes to an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public static void Subscribe(string eventName, EventHandler<EventArgs> handler)
        => Service.Subscribe(eventName, handler);

    /// <summary>
    /// Subscribes to a typed event
    /// </summary>
    /// <typeparam name="TEventArgs">The event args type</typeparam>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public static void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs
        => Service.Subscribe(eventName, handler);

    /// <summary>
    /// Unsubscribes from an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public static void Unsubscribe(string eventName, EventHandler<EventArgs> handler)
        => Service.Unsubscribe(eventName, handler);

    /// <summary>
    /// Unsubscribes from a typed event
    /// </summary>
    /// <typeparam name="TEventArgs">The event args type</typeparam>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public static void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs
        => Service.Unsubscribe(eventName, handler);
}
