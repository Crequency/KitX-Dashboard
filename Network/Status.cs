namespace KitX_Dashboard.Network;

/// <summary>
/// 网络状态
/// </summary>
internal enum NetworkStatus
{
    /// <summary>
    /// 未知状态
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 正在连接
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// 已经连接
    /// </summary>
    Connected = 2,

    /// <summary>
    /// 连接断开
    /// </summary>
    Disconnected = 3,

    /// <summary>
    /// 发生错误
    /// </summary>
    Errored = 4,
}

/// <summary>
/// 网络类型
/// </summary>
internal enum NetworkType
{
    /// <summary>
    /// 未知类型
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 服务器
    /// </summary>
    Server = 1,

    /// <summary>
    /// 客户端
    /// </summary>
    Client = 2,
}

/// <summary>
/// 服务器状态
/// </summary>
internal enum ServerStatus
{
    /// <summary>
    /// 未知状态
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 启动中
    /// </summary>
    Starting = 1,

    /// <summary>
    /// 运行中
    /// </summary>
    Running = 2,

    /// <summary>
    /// 停止中
    /// </summary>
    Stopping = 3,

    /// <summary>
    /// 等待中
    /// </summary>
    Pending = 4,

    /// <summary>
    /// 发生错误
    /// </summary>
    Errored = 5,
}

/// <summary>
/// 客户端状态
/// </summary>
internal enum ClientStatus
{
    /// <summary>
    /// 未知状态
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 连接中
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// 运行中
    /// </summary>
    Running = 2,

    /// <summary>
    /// 断开连接中
    /// </summary>
    Disconnecting = 3,

    /// <summary>
    /// 等待中
    /// </summary>
    Pending = 4,

    /// <summary>
    /// 发生错误
    /// </summary>
    Errored = 5,
}
