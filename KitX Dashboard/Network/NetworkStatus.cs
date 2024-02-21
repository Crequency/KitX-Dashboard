namespace KitX.Dashboard.Network;

public enum ServerStatus
{
    Unknown = 0,

    Starting = 1,

    Running = 2,

    Stopping = 3,

    Pending = 4,

    Errored = 5,
}

public enum ClientStatus
{
    Unknown = 0,

    Connecting = 1,

    Running = 2,

    Disconnecting = 3,

    Pending = 4,

    Errored = 5,
}
