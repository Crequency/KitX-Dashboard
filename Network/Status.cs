﻿namespace KitX_Dashboard.Network;

internal enum NetworkStatus
{
    Unknown = 0,
    Connecting = 1,
    Connected = 2,
    Disconnected = 3,
    Errored = 4,
}

internal enum ServerStatus
{
    Unknown = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Pending = 4,
    Errored = 5,
}

internal enum NetworkType
{
    Unknown = 0,
    Server = 1,
    Client = 2,
}