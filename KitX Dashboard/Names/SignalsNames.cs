namespace KitX.Dashboard.Names;

/// <summary>
/// Signal names for <c>SignalTasksManager</c> coordination. Only the member NAMES are
/// consumed (via <c>nameof(...)</c>); the values are never read (D13.2).
/// </summary>
internal static class SignalsNames
{
    internal const string MainWindowInitSignal = nameof(MainWindowInitSignal);

    internal const string MainWindowOpenedSignal = nameof(MainWindowOpenedSignal);

    internal const string FinishedFindingNetworkInterfacesSignal = nameof(FinishedFindingNetworkInterfacesSignal);

    internal const string FileWatcherManagerInitializedSignal = nameof(FileWatcherManagerInitializedSignal);
}
