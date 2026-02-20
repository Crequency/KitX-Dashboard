# KitX Dashboard API 文档

## 文档信息

- **项目名称**: KitX Dashboard
- **文档版本**: v1.0
- **创建日期**: 2026-02-19
- **目的**: 描述 KitX Dashboard 的公共 API

---

## 1. 配置管理

### 1.1 IConfigService

配置管理服务接口

```csharp
namespace KitX.Core.Contract.Configuration;

public interface IConfigService
{
    /// <summary>
    /// Gets the application configuration
    /// </summary>
    IAppConfig AppConfig { get; }

    /// <summary>
    /// Gets the plugins configuration
    /// </summary>
    IPluginsConfig PluginsConfig { get; }

    /// <summary>
    /// Gets the security configuration
    /// </summary>
    ISecurityConfig SecurityConfig { get; }

    /// <summary>
    /// Loads all configurations from files
    /// </summary>
    void Load();

    /// <summary>
    /// Saves all configurations to files
    /// </summary>
    void SaveAll();

    /// <summary>
    /// Reloads all configurations from files
    /// </summary>
    void Reload();

    /// <summary>
    /// Event raised when configuration changes
    /// </summary>
    event EventHandler<ConfigChangedEventArgs>? ConfigChanged;
}
```

### 1.2 IAppConfig

完整应用配置接口 (包含 8 个子配置)

```csharp
public interface IAppConfig
{
    IAppConf App { get; set; }
    IWindowsConf Windows { get; set; }
    IPagesConf Pages { get; set; }
    IWebConf Web { get; set; }
    ILogConf Log { get; set; }
    IIOConf IO { get; set; }
    IActivityConf Activity { get; set; }
    ILoadersConf Loaders { get; set; }
}
```

### 1.3 IAppConf

应用基础配置接口

```csharp
public interface IAppConf
{
    string IconFileName { get; set; }
    string CoverIconFileName { get; set; }
    string AppLanguage { get; set; }
    string Theme { get; set; }
    string ThemeColor { get; set; }
    Dictionary<string, string> SurpportLanguages { get; set; }
    string LocalPluginsFileFolder { get; set; }
    string LocalPluginsDataFolder { get; set; }
    bool DeveloperSetting { get; set; }
    bool ShowAnnouncementWhenStart { get; set; }
    ulong RanTime { get; set; }
    int LastBreakAfterExit { get; set; }
}
```

### 1.4 IWindowsConf

窗口配置接口

```csharp
public interface IWindowsConf
{
    IMainWindowConf MainWindow { get; set; }
    IAnnouncementWindowConf AnnouncementWindow { get; set; }
}
```

### 1.5 IMainWindowConf

主窗口配置接口

```csharp
public interface IMainWindowConf
{
    Resolution Size { get; set; }
    Distances Location { get; set; }
    WindowState WindowState { get; set; }
    bool IsHidden { get; set; }
    Dictionary<string, string> Tags { get; set; }
    bool EnabledMica { get; set; }
    int GreetingTextCount_Morning { get; set; }
    int GreetingTextCount_Noon { get; set; }
    int GreetingTextCount_AfterNoon { get; set; }
    int GreetingTextCount_Evening { get; set; }
    int GreetingTextCount_Night { get; set; }
    int GreetingUpdateInterval { get; set; }
}
```

### 1.6 IPagesConf / ISettingsPageConf

页面配置接口

```csharp
public interface IPagesConf
{
    IHomePageConf Home { get; set; }
    IDevicePageConf Device { get; set; }
    IMarketPageConf Market { get; set; }
    ISettingsPageConf Settings { get; set; }
}

public interface ISettingsPageConf
{
    NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode { get; set; }
    string SelectedViewName { get; set; }
    bool PaletteAreaExpanded { get; set; }
    bool WebRelatedAreaExpanded { get; set; }
    bool WebRelatedAreaOfNetworkInterfacesExpanded { get; set; }
    bool LogRelatedAreaExpanded { get; set; }
    bool UpdateRelatedAreaExpanded { get; set; }
    bool AboutAreaExpanded { get; set; }
    bool AuthorsAreaExpanded { get; set; }
    bool LinksAreaExpanded { get; set; }
    bool ThirdPartyLicensesAreaExpanded { get; set; }
    bool IsNavigationViewPaneOpened { get; set; }
}
```

### 1.7 IWebConf

网络配置接口

```csharp
public interface IWebConf
{
    double DelayStartSeconds { get; set; }
    string ApiServer { get; set; }
    string ApiPath { get; set; }
    int DevicesViewRefreshDelay { get; set; }
    List<string>? AcceptedNetworkInterfaces { get; set; }
    int? UserSpecifiedDevicesServerPort { get; set; }
    int? UserSpecifiedPluginsServerPort { get; set; }
    int UdpPortSend { get; set; }
    int UdpPortReceive { get; set; }
    int UdpSendFrequency { get; set; }
    string UdpBroadcastAddress { get; set; }
    string IPFilter { get; set; }
    int SocketBufferSize { get; set; }
    int DeviceInfoTTLSeconds { get; set; }
    bool DisableRemovingOfflineDeviceCard { get; set; }
    string UpdateServer { get; set; }
    string UpdatePath { get; set; }
    string UpdateDownloadPath { get; set; }
    string UpdateChannel { get; set; }
    string UpdateSource { get; set; }
    int DebugServicesServerPort { get; set; }
}
```

### 1.8 其他配置接口

```csharp
public interface ILogConf
{
    long LogFileSingleMaxSize { get; set; }
    string LogFilePath { get; set; }
    string LogTemplate { get; set; }
    int LogFileMaxCount { get; set; }
    int LogFileFlushInterval { get; set; }
    LogEventLevel LogLevel { get; set; }
}

public interface IIOConf
{
    int UpdatingCheckPerThreadFilesCount { get; set; }
    int OperatingSystemVersionUpdateInterval { get; set; }
}

public interface IActivityConf
{
    int TotalRecorded { get; set; }
}

public interface ILoadersConf
{
    string InstallPath { get; set; }
}

public interface IAnnouncementConfig
{
    List<string> Accepted { get; set; }
    string? ConfigFileLocation { get; set; }
}
```

### 1.9 枚举类型

```csharp
public enum WindowState
{
    Normal,
    Minimized,
    Maximized,
    FullScreen,
    NonInteractive
}

public enum NavigationViewPaneDisplayMode
{
    Auto = 0,
    Left = 1,
    Top = 2,
    LeftCompact = 3,
    LeftMinimal = 4
}
```

---

## 2. 设备管理

### 2.1 IDeviceService

设备管理服务接口

```csharp
namespace KitX.Core.Contract.Device;

public interface IDeviceService
{
    /// <summary>
    /// Gets the discovered devices list
    /// </summary>
    IReadOnlyList<IDeviceCase> DiscoveredDevices { get; }

    /// <summary>
    /// Gets the authorized devices list
    /// </summary>
    IReadOnlyList<IDeviceCase> AuthorizedDevices { get; }

    /// <summary>
    /// Gets the self device information
    /// </summary>
    DeviceInfo SelfDeviceInfo { get; }

    /// <summary>
    /// Gets a value indicating whether this device is the main device
    /// </summary>
    bool IsMainDevice { get; }

    /// <summary>
    /// Authorizes a device
    /// </summary>
    Task<bool> AuthorizeDeviceAsync(string deviceId, string deviceKey);

    /// <summary>
    /// Unauthorizes a device
    /// </summary>
    Task<bool> UnauthorizeDeviceAsync(string deviceId);

    /// <summary>
    /// Connects to a device
    /// </summary>
    Task<bool> ConnectToDeviceAsync(string deviceId);

    /// <summary>
    /// Event raised when a device is discovered
    /// </summary>
    event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

    /// <summary>
    /// Event raised when a device goes offline
    /// </summary>
    event EventHandler<DeviceOfflineEventArgs>? DeviceOffline;

    /// <summary>
    /// Event raised when the main device changes
    /// </summary>
    event EventHandler<MainDeviceChangedEventArgs>? MainDeviceChanged;
}
```

### 2.2 IDeviceDiscoveryService

设备发现服务接口

```csharp
public interface IDeviceDiscoveryService
{
    /// <summary>
    /// Gets the port the discovery service is running on
    /// </summary>
    int? Port { get; }

    /// <summary>
    /// Starts the device discovery service
    /// </summary>
    IDeviceDiscoveryService Run();

    /// <summary>
    /// Stops the device discovery service
    /// </summary>
    void Stop();

    /// <summary>
    /// Event raised when a device is discovered
    /// </summary>
    event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

    /// <summary>
    /// Event raised when a device goes offline
    /// </summary>
    event EventHandler<DeviceOfflineEventArgs>? DeviceOffline;
}
```

### 2.3 IDeviceServer

设备服务器接口

```csharp
public interface IDeviceServer
{
    /// <summary>
    /// Gets the port the server is running on
    /// </summary>
    int? Port { get; }

    /// <summary>
    /// Starts the device server
    /// </summary>
    IDeviceServer Run();

    /// <summary>
    /// Stops the device server
    /// </summary>
    void Stop();
}
```

---

## 3. 插件管理

### 3.1 IPluginService

插件管理服务接口

```csharp
namespace KitX.Core.Contract.Plugin;

public interface IPluginService
{
    /// <summary>
    /// Gets all installed plugins
    /// </summary>
    IReadOnlyList<IPluginInstallation> GetInstalledPlugins();

    /// <summary>
    /// Gets a plugin by its ID
    /// </summary>
    IPluginInstallation? GetPlugin(Guid pluginId);

    /// <summary>
    /// Imports a plugin package (.kxp file)
    /// </summary>
    Task<bool> ImportPluginAsync(string kxpFilePath);

    /// <summary>
    /// Removes a plugin
    /// </summary>
    Task<bool> RemovePluginAsync(Guid pluginId);

    /// <summary>
    /// Starts a plugin
    /// </summary>
    Task<bool> StartPluginAsync(Guid pluginId);

    /// <summary>
    /// Stops a plugin
    /// </summary>
    Task<bool> StopPluginAsync(Guid pluginId);

    /// <summary>
    /// Calls a plugin function
    /// </summary>
    Task<object?> CallPluginFunctionAsync(Guid pluginId, string functionName, Dictionary<string, object>? parameters = null);

    /// <summary>
    /// Event raised when plugin status changes
    /// </summary>
    event EventHandler<PluginStatusChangedEventArgs>? PluginStatusChanged;
}
```

### 3.2 IPluginServer

插件服务器接口

```csharp
public interface IPluginServer
{
    /// <summary>
    /// Gets the port the server is running on
    /// </summary>
    int? Port { get; }

    /// <summary>
    /// Starts the plugin server
    /// </summary>
    IPluginServer Run();

    /// <summary>
    /// Stops the plugin server
    /// </summary>
    void Stop();

    /// <summary>
    /// Finds a connector for a specific plugin
    /// </summary>
    IPluginConnector? FindConnector(PluginInfo pluginInfo);

    /// <summary>
    /// Event raised when server port changes
    /// </summary>
    event EventHandler<int>? PortChanged;

    /// <summary>
    /// Event raised when a plugin registers with the server
    /// </summary>
    event EventHandler<PluginRegisteredEventArgs>? PluginRegistered;

    /// <summary>
    /// Event raised when a plugin unregisters/disconnects from the server
    /// </summary>
    event EventHandler<PluginUnregisteredEventArgs>? PluginUnregistered;
}
```

### 3.3 IPluginConnector

插件连接器接口

```csharp
public interface IPluginConnector
{
    /// <summary>
    /// Gets the connection ID
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// Gets the plugin info
    /// </summary>
    PluginInfo? PluginInfo { get; }

    /// <summary>
    /// Sends a request to the plugin
    /// </summary>
    void Request(object request);

    /// <summary>
    /// Event raised when a plugin response is received
    /// </summary>
    event EventHandler<PluginResponseEventArgs>? PluginResponse;

    /// <summary>
    /// Event raised when plugin reports status
    /// </summary>
    event EventHandler<PluginStatusReportEventArgs>? StatusReport;
}
```

---

## 4. 安全管理

### 4.1 ISecurityService

安全管理服务接口

```csharp
namespace KitX.Core.Contract.Security;

public interface ISecurityService
{
    /// <summary>
    /// Gets all device keys
    /// </summary>
    IReadOnlyList<IDeviceKey> GetDeviceKeys();

    /// <summary>
    /// Adds a device key
    /// </summary>
    bool AddDeviceKey(string macAddress, string deviceName, string publicKey);

    /// <summary>
    /// Removes a device key
    /// </summary>
    bool RemoveDeviceKey(string macAddress);

    /// <summary>
    /// Checks if a device is authorized
    /// </summary>
    bool IsDeviceAuthorized(DeviceLocator device);

    /// <summary>
    /// Encrypts a string
    /// </summary>
    Task<string> EncryptStringAsync(string content, string targetDeviceMacAddress);

    /// <summary>
    /// Decrypts a string
    /// </summary>
    Task<string> DecryptStringAsync(string encryptedContent, string sourceDeviceMacAddress);

    /// <summary>
    /// Computes a hash
    /// </summary>
    string ComputeHash(string content);
}
```

---

## 5. 活动记录

### 5.1 IActivityService

活动记录服务接口

```csharp
namespace KitX.Core.Contract.Activity;

public interface IActivityService
{
    /// <summary>
    /// Records an activity
    /// </summary>
    void RecordActivity(string type, Dictionary<string, object>? details = null);

    /// <summary>
    /// Gets activities
    /// </summary>
    IList<IActivity> GetActivities(DateTime? startDate = null, DateTime? endDate = null, int limit = 100);

    /// <summary>
    /// Gets activity statistics
    /// </summary>
    IActivityStatistics GetStatistics(DateTime startDate, DateTime endDate);

    /// <summary>
    /// Event raised when activities are updated
    /// </summary>
    event EventHandler? ActivitiesUpdated;
}
```

---

## 6. 统计服务

### 6.1 IStatisticsService

统计服务接口

```csharp
namespace KitX.Core.Contract.Statistics;

public interface IStatisticsService
{
    /// <summary>
    /// Starts statistics collection
    /// </summary>
    void Start();

    /// <summary>
    /// Stops statistics collection
    /// </summary>
    void Stop();

    /// <summary>
    /// Gets usage statistics
    /// </summary>
    IUsageStatistics GetUsageStatistics(DateTime startDate, DateTime endDate);
}
```

---

## 7. 工作流

### 7.1 IWorkflowService

工作流服务接口

```csharp
namespace KitX.Core.Contract.Workflow;

public interface IWorkflowService
{
    /// <summary>
    /// Gets the workflow list
    /// </summary>
    IReadOnlyList<IWorkflowCase> GetWorkflows();

    /// <summary>
    /// Adds a workflow
    /// </summary>
    void AddWorkflow(IWorkflowCase workflow);

    /// <summary>
    /// Removes a workflow
    /// </summary>
    void RemoveWorkflow(string workflowId);

    /// <summary>
    /// Runs a workflow
    /// </summary>
    Task<bool> RunWorkflowAsync(string workflowId);

    /// <summary>
    /// Stops a workflow
    /// </summary>
    Task<bool> StopWorkflowAsync(string workflowId);

    /// <summary>
    /// Executes a workflow script
    /// </summary>
    Task<object?> ExecuteScriptAsync(string script, Dictionary<string, object>? parameters = null);

    /// <summary>
    /// Executes workflow script codes with plugin dependencies
    /// </summary>
    Task<string?> ExecuteCodesAsync(
        string code,
        List<PluginInfo>? requiredPlugins = null,
        bool includeTimestamp = true,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the plugin manager
    /// </summary>
    void InitializePluginManager();

    /// <summary>
    /// Updates the available plugins list
    /// </summary>
    void UpdateAvailablePlugins(List<PluginInfo> plugins);
}
```

### 7.2 IPluginServiceProvider

插件服务提供者接口

```csharp
public interface IPluginServiceProvider
{
    /// <summary>
    /// Gets running plugins
    /// </summary>
    IEnumerable<PluginInfo> GetRunningPlugins();

    /// <summary>
    /// Finds a plugin by name
    /// </summary>
    PluginInfo? FindPlugin(string pluginName);

    /// <summary>
    /// Finds a connector for a plugin
    /// </summary>
    object? FindConnector(PluginInfo pluginInfo);

    /// <summary>
    /// Sends a request asynchronously
    /// </summary>
    Task SendRequestAsync(object connector, object request);

    /// <summary>
    /// Subscribes to plugin responses
    /// </summary>
    void SubscribeToResponses(Action<string, string> responseHandler);
}
```

### 7.2 IWorkflowCase

工作流实例接口

```csharp
namespace KitX.Core.Contract.Workflow;

public interface IWorkflowCase
{
    /// <summary>
    /// Gets the workflow ID
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the workflow name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the workflow description
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the icon path
    /// </summary>
    string IconPath { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the workflow is running
    /// </summary>
    bool IsRunning { get; set; }

    /// <summary>
    /// Gets or sets the script file path
    /// </summary>
    string? ScriptPath { get; set; }
}
```

---

## 8. 事件系统

### 8.1 IEventService

事件服务接口

```csharp
namespace KitX.Core.Contract.Event;

public interface IEventService
{
    /// <summary>
    /// Subscribes to an event
    /// </summary>
    void Subscribe(string eventName, EventHandler<EventArgs> handler);

    /// <summary>
    /// Unsubscribes from an event
    /// </summary>
    void Unsubscribe(string eventName, EventHandler<EventArgs> handler);

    /// <summary>
    /// Publishes an event
    /// </summary>
    void Publish(string eventName, EventArgs args);

    /// <summary>
    /// Subscribes to a typed event
    /// </summary>
    void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs;

    /// <summary>
    /// Unsubscribes from a typed event
    /// </summary>
    void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs;

    /// <summary>
    /// Publishes a typed event
    /// </summary>
    void Publish<TEventArgs>(string eventName, TEventArgs args)
        where TEventArgs : EventArgs;
}
```

### 8.2 EventNames

事件名称常量

```csharp
namespace KitX.Core.Event;

public static class EventNames
{
    public const string LanguageChanged = "LanguageChanged";
    public const string GreetingTextIntervalUpdated = "GreetingTextIntervalUpdated";
    public const string AppConfigChanged = "AppConfigChanged";
    public const string PluginsConfigChanged = "PluginsConfigChanged";
    public const string MicaOpacityChanged = "MicaOpacityChanged";
    public const string DevelopSettingsChanged = "DevelopSettingsChanged";
    public const string LogConfigUpdated = "LogConfigUpdated";
    public const string ThemeConfigChanged = "ThemeConfigChanged";
    public const string UseStatisticsChanged = "UseStatisticsChanged";
    public const string DevicesServerPortChanged = "DevicesServerPortChanged";
    public const string PluginsServerPortChanged = "PluginsServerPortChanged";
    public const string OnActivitiesUpdated = "OnActivitiesUpdated";
    public const string OnReceiveCancelExchangingDeviceKey = "OnReceiveCancelExchangingDeviceKey";
    public const string OnExiting = "OnExiting";
    public const string OnReceivingDeviceInfo = "OnReceivingDeviceInfo";
    public const string OnConfigHotReloaded = "OnConfigHotReloaded";
    public const string OnAcceptingDeviceKey = "OnAcceptingDeviceKey";
}
```

### 8.3 过时的 API (Legacy / Obsolete)

以下 API 已标记为 `[Obsolete]`，**不建议使用**，仅用于向后兼容：

#### EventService 静态方法

```csharp
[Obsolete("Use IEventService.Publish with event names instead")]
public static void Invoke(string eventName, object[]? objects = null)
```

#### 静态事件 (已过时，使用 IEventService 替代)

| 静态事件 | 过时替代方案 |
|----------|--------------|
| `EventService.LanguageChanged` | `IEventService.Publish(EventNames.LanguageChanged, args)` |
| `EventService.GreetingTextIntervalUpdated` | `IEventService.Publish(EventNames.GreetingTextIntervalUpdated, args)` |
| `EventService.AppConfigChanged` | `IEventService.Publish(EventNames.AppConfigChanged, args)` |
| `EventService.PluginsConfigChanged` | `IEventService.Publish(EventNames.PluginsConfigChanged, args)` |
| `EventService.MicaOpacityChanged` | `IEventService.Publish(EventNames.MicaOpacityChanged, args)` |
| `EventService.DevelopSettingsChanged` | `IEventService.Publish(EventNames.DevelopSettingsChanged, args)` |
| `EventService.LogConfigUpdated` | `IEventService.Publish(EventNames.LogConfigUpdated, args)` |
| `EventService.ThemeConfigChanged` | `IEventService.Publish(EventNames.ThemeConfigChanged, args)` |
| `EventService.UseStatisticsChanged` | `IEventService.Publish(EventNames.UseStatisticsChanged, args)` |
| `EventService.DevicesServerPortChanged` | `IEventService.Publish(EventNames.DevicesServerPortChanged, args)` |
| `EventService.PluginsServerPortChanged` | `IEventService.Publish(EventNames.PluginsServerPortChanged, args)` |
| `EventService.OnActivitiesUpdated` | `IEventService.Publish(EventNames.OnActivitiesUpdated, args)` |
| `EventService.OnReceiveCancelExchangingDeviceKey` | `IEventService.Publish(EventNames.OnReceiveCancelExchangingDeviceKey, args)` |
| `EventService.OnExiting` | `IEventService.Publish(EventNames.OnExiting, args)` |
| `EventService.OnReceivingDeviceInfo` | `IEventService.Publish(EventNames.OnReceivingDeviceInfo, args)` |
| `EventService.OnConfigHotReloaded` | `IEventService.Publish(EventNames.OnConfigHotReloaded, args)` |
| `EventService.OnAcceptingDeviceKey` | `IEventService.Publish(EventNames.OnAcceptingDeviceKey, args)` |

**迁移建议**：
- 使用 `IEventService` 接口代替 `EventService` 静态类
- 通过依赖注入获取 `IEventService` 实例
- 使用 `EventNames` 常量定义事件名称

---

## 9. 任务调度

### 9.1 ITasksService

任务服务接口

```csharp
namespace KitX.Core.Contract.Tasks;

public interface ITasksService
{
    /// <summary>
    /// Runs a synchronous task
    /// </summary>
    void RunTask(Action task, string? taskName = null);

    /// <summary>
    /// Runs an asynchronous task
    /// </summary>
    Task RunTaskAsync(Func<Task> task, string? taskName = null);
}
```

---

## 10. 文件监控

### 10.1 IFileWatcherService

文件监控服务接口

```csharp
namespace KitX.Core.Contract.FileWatcher;

public interface IFileWatcherService
{
    /// <summary>
    /// Registers a file watcher
    /// </summary>
    void RegisterWatcher(string filePath, FileSystemEventHandler onChanged);

    /// <summary>
    /// Unregisters a file watcher
    /// </summary>
    void UnregisterWatcher(string filePath);

    /// <summary>
    /// Clears all file watchers
    /// </summary>
    void Clear();
}
```

---

## 11. 全局热键

### 11.1 IKeyHookService

热键服务接口

```csharp
namespace KitX.Core.Contract.Hotkey;

public interface IKeyHookService
{
    /// <summary>
    /// Starts the key hook
    /// </summary>
    void StartHook();

    /// <summary>
    /// Stops the key hook
    /// </summary>
    void StopHook();

    /// <summary>
    /// Registers a hotkey handler
    /// </summary>
    void RegisterHotKeyHandler(string keysSequence, Action handler);

    /// <summary>
    /// Unregisters a hotkey handler
    /// </summary>
    void UnregisterHotKeyHandler(string keysSequence);
}
```

---

## 12. 公告系统

### 12.1 IAnnouncementService

公告服务接口

```csharp
namespace KitX.Core.Contract.Announcement;

public interface IAnnouncementService
{
    /// <summary>
    /// Checks for new announcements
    /// </summary>
    Task<IReadOnlyList<IAnnouncement>> CheckNewAnnouncementsAsync();

    /// <summary>
    /// Marks an announcement as read
    /// </summary>
    void MarkAsRead(string announcementId);

    /// <summary>
    /// Gets all read announcement IDs
    /// </summary>
    IReadOnlyList<string> GetReadAnnouncementIds();

    /// <summary>
    /// Event raised when new announcements are available
    /// </summary>
    event EventHandler<NewAnnouncementsEventArgs>? NewAnnouncementsAvailable;
}
```

---

## 13. 事件参数

### 13.1 配置变更事件

```csharp
public class ConfigChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the configuration type (e.g., "App", "Plugins", "Security")
    /// </summary>
    public string ConfigType { get; set; }

    /// <summary>
    /// Gets or sets the property name that changed
    /// </summary>
    public string PropertyName { get; set; }

    /// <summary>
    /// Gets or sets the old value
    /// </summary>
    public object? OldValue { get; set; }

    /// <summary>
    /// Gets or sets the new value
    /// </summary>
    public object? NewValue { get; set; }
}
```

### 13.2 设备发现事件

```csharp
public class DeviceDiscoveredEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the device information
    /// </summary>
    public DeviceInfo? DeviceInfo { get; set; }
}
```

### 13.3 设备离线事件

```csharp
public class DeviceOfflineEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the device ID
    /// </summary>
    public string DeviceId { get; set; }
}
```

### 13.4 主设备变更事件

```csharp
public class MainDeviceChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the old main device ID
    /// </summary>
    public string OldMainDeviceId { get; set; }

    /// <summary>
    /// Gets or sets the new main device ID
    /// </summary>
    public string NewMainDeviceId { get; set; }
}
```

### 13.5 插件状态变更事件

```csharp
public class PluginStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the plugin ID
    /// </summary>
    public Guid PluginId { get; set; }

    /// <summary>
    /// Gets or sets the plugin name
    /// </summary>
    public string PluginName { get; set; }

    /// <summary>
    /// Gets or sets the old status
    /// </summary>
    public PluginStatus OldStatus { get; set; }

    /// <summary>
    /// Gets or sets the new status
    /// </summary>
    public PluginStatus NewStatus { get; set; }
}

public enum PluginStatus
{
    Unknown,
    Installed,
    Running,
    Stopped,
    Error
}
```

---

## 14. 依赖注入扩展

### 14.1 AddCoreServices

在 DI 容器中注册所有核心服务

```csharp
namespace KitX.Core.DI;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds all KitX Core services to the dependency injection container
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddCoreServices(this IServiceCollection services);
}
```

---

**文档结束**

*本文档描述了 KitX Dashboard 的公共 API。*
