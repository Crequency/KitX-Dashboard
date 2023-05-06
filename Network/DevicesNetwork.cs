using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views.Pages.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using Timer = System.Timers.Timer;

namespace KitX_Dashboard.Network;

internal class DevicesNetwork
{
    internal static DevicesServer? devicesServer = null;

    internal static DevicesClient? devicesClient = null;

    private static void InitEvents()
    {
        EventService.OnReceivingDeviceInfoStruct += dis =>
        {
            if (dis.IsMainDevice && dis.DeviceServerBuildTime < GlobalInfo.ServerBuildTime)
            {
                Stop();

                Watch4MainDevice();

                Log.Information($"In DevicesService: Watched earlier built server. " +
                    $"DeviceServerAddress: {dis.IPv4}:{dis.DeviceServerPort} " +
                    $"DeviceServerBuildTime: {dis.DeviceServerBuildTime}");
            }
        };
    }

    private static readonly object _receivedDeviceInfoStruct4WatchLock = new();

    internal static List<DeviceInfoStruct>? receivedDeviceInfoStruct4Watch;

    internal static readonly Queue<DeviceInfoStruct> deviceInfoStructs = new();

    private static readonly object AddDeviceCard2ViewLock = new();

    private static bool KeepCheckAndRemoveTaskRunning = false;

    /// <summary>
    /// 判断设备是否应该标记为离线
    /// </summary>
    /// <param name="info">设备广播信息</param>
    /// <returns>是否离线</returns>
    private static bool CheckDeviceIsOffline(DeviceInfoStruct info)
        => DateTime.UtcNow - info.SendTime.ToUniversalTime()
           > new TimeSpan(0, 0, ConfigManager.AppConfig.Web.DeviceInfoStructTTLSeconds);

    /// <summary>
    /// 判断是否是本机卡片
    /// </summary>
    /// <param name="info">设备信息</param>
    /// <returns>是否是本机卡片</returns>
    private static bool CheckIsCurrentMachine(DeviceInfoStruct info)
    {
        var self = DevicesDiscoveryServer.DefaultDeviceInfoStruct;
        return info.DeviceMacAddress.Equals(self.DeviceMacAddress)
            && info.DeviceName.Equals(self.DeviceName);
    }

    /// <summary>
    /// 更新数据源中的设备信息并添加数据源中没有的设备卡片
    /// </summary>
    private static void UpdateSourceAndAddCards()
    {
        var need2addDevicesCount = 0;
        var thisTurnAdded = new List<int>();

        while (deviceInfoStructs.Count > 0)
        {
            var deviceInfoStruct = deviceInfoStructs.Dequeue();
            var hashCode = deviceInfoStruct.GetHashCode();
            var findThis = thisTurnAdded.Contains(hashCode);
            if (findThis) continue;
            foreach (var item in Program.DeviceCards)
            {
                if (item.viewModel.DeviceInfo.DeviceName.Equals(deviceInfoStruct.DeviceName))
                {
                    item.viewModel.DeviceInfo = deviceInfoStruct;
                    findThis = true;
                    break;
                }
            }
            if (!findThis)
            {
                thisTurnAdded.Add(hashCode);
                lock (AddDeviceCard2ViewLock)
                {
                    ++need2addDevicesCount;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    lock (AddDeviceCard2ViewLock)
                    {
                        Program.DeviceCards.Add(new(deviceInfoStruct));
                        --need2addDevicesCount;
                    }
                });
            }
        }

        while (need2addDevicesCount != 0) ;
    }

    /// <summary>
    /// 移除离线设备
    /// </summary>
    private static void RemoveOfflineCards()
    {
        List<DeviceCard> DevicesNeed2BeRemoved = new();
        foreach (var item in Program.DeviceCards)
        {
            var info = item.viewModel.DeviceInfo;
            if (CheckDeviceIsOffline(info)) DevicesNeed2BeRemoved.Add(item);
        }

        var removeDeviceTaskRunning = true;

        Dispatcher.UIThread.Post(() =>
        {
            lock (AddDeviceCard2ViewLock)
            {
                foreach (var item in DevicesNeed2BeRemoved)
                    Program.DeviceCards.Remove(item);
            }
            removeDeviceTaskRunning = false;
        });

        while (removeDeviceTaskRunning) ;
    }

    /// <summary>
    /// 移除重复设备卡片
    /// </summary>
    private static void RemoveDumplicatedCards()
    {
        List<string>
            MacAddressVisited = new(),
            IPv4AddressVisited = new(),
            IPv6AddressVisited = new();
        List<DeviceCard> DevicesNeed2BeRemoved = new();

        foreach (var item in Program.DeviceCards)
        {
            var info = item.viewModel.DeviceInfo;
            if (
                MacAddressVisited.Contains(info.DeviceMacAddress) ||
                IPv4AddressVisited.Contains(info.IPv4) ||
                IPv6AddressVisited.Contains(info.IPv6)
               )
            {
                DevicesNeed2BeRemoved.Add(item);
                continue;
            }
            MacAddressVisited.Add(info.DeviceMacAddress);
            IPv4AddressVisited.Add(info.IPv4);
            IPv6AddressVisited.Add(info.IPv6);
        }

        var removeDeviceTaskRunning = true;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var item in DevicesNeed2BeRemoved)
            {
                lock (AddDeviceCard2ViewLock)
                {
                    Program.DeviceCards.Remove(item);
                }
            }
            removeDeviceTaskRunning = false;
        });

        while (removeDeviceTaskRunning) ;
    }

    /// <summary>
    /// 移动本机设备卡片到第一个
    /// </summary>
    private static void MoveSelfCard2First()
    {
        var index = 0;
        var moveSelfCardTaskRunning = true;

        foreach (var item in Program.DeviceCards)
        {
            var info = item.viewModel.DeviceInfo;

            if (CheckIsCurrentMachine(info))
            {
                if (index != 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            Program.DeviceCards.Move(index, 0);
                        }
                        catch (Exception e)
                        {
                            Log.Warning(e, $"Can't move self 2 first. {e.Message}");
                        }
                        moveSelfCardTaskRunning = false;
                    });

                    while (moveSelfCardTaskRunning) ;
                }
                break;
            }
            ++index;
        }
    }

    /// <summary>
    /// 持续检查并移除
    /// </summary>
    private static void KeepCheckAndRemove()
    {
        var timer = new Timer()
        {
            Interval = ConfigManager.AppConfig.Web.DevicesViewRefreshDelay,
            AutoReset = true
        };

        timer.Elapsed += (_, _) =>
        {
            try
            {
                var location = $"{nameof(DevicesServer)}.{nameof(KeepCheckAndRemove)}()";
                if (KeepCheckAndRemoveTaskRunning)
                    Log.Information($"In {location}: Timer elapsed and skip task.");
                else
                {
                    Log.Information($"In {location}: Timer elapsed and run task.");

                    KeepCheckAndRemoveTaskRunning = true;

                    UpdateSourceAndAddCards();

                    //RemoveDumplicatedCards();

                    if (!ConfigManager.AppConfig.Web.DisableRemovingOfflineDeviceCard)
                        RemoveOfflineCards();

                    MoveSelfCard2First();

                    KeepCheckAndRemoveTaskRunning = false;
                }
            }
            catch (Exception ex)
            {
                var location = $"{nameof(DevicesNetwork)}.{nameof(KeepCheckAndRemove)}()";
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        };

        timer.Start();

        EventService.ConfigSettingsChanged += () =>
        {
            timer.Interval = ConfigManager.AppConfig.Web.DevicesViewRefreshDelay;
        };
    }

    /// <summary>
    /// 更新收到的UDP包
    /// </summary>
    /// <param name="deviceInfo">设备信息结构</param>
    internal static void Update(DeviceInfoStruct deviceInfo)
    {
        deviceInfoStructs.Enqueue(deviceInfo);

        if (receivedDeviceInfoStruct4Watch is not null)
        {
            lock (_receivedDeviceInfoStruct4WatchLock)
            {
                receivedDeviceInfoStruct4Watch.Add(deviceInfo);
            }
        }

        EventService.Invoke(nameof(EventService.OnReceivingDeviceInfoStruct), deviceInfo);
    }

    /// <summary>
    /// 观察主控
    /// </summary>
    internal static void Watch4MainDevice()
    {
        var location = $"{nameof(DevicesNetwork)}.{nameof(Watch4MainDevice)}";

        new Thread(() =>
        {
            receivedDeviceInfoStruct4Watch = new();

            var checkedTime = 0;
            var hadMainDevice = false;
            var earliestBuiltServerTime = DateTime.UtcNow;
            var serverPort = 0;
            var serverAddress = string.Empty;

            while (checkedTime < 7)
            {
                try
                {
                    if (receivedDeviceInfoStruct4Watch is null) continue;

                    lock (_receivedDeviceInfoStruct4WatchLock)
                    {
                        foreach (var item in receivedDeviceInfoStruct4Watch)
                        {
                            if (item.IsMainDevice)
                            {
                                if (item.DeviceServerBuildTime.ToUniversalTime() < earliestBuiltServerTime)
                                {
                                    serverPort = item.DeviceServerPort;
                                    serverAddress = item.IPv4;
                                }
                                hadMainDevice = true;
                            }
                        }
                    }

                    ++checkedTime;

                    Log.Information($"In {location}: Watched for {checkedTime} times.");

                    if (checkedTime == 7)
                    {
                        receivedDeviceInfoStruct4Watch?.Clear();
                        receivedDeviceInfoStruct4Watch = null;

                        WatchingOver(hadMainDevice, serverAddress, serverPort);
                    }

                    Thread.Sleep(1 * 1000); //  Sleep 1 second.
                }
                catch (Exception e)
                {
                    receivedDeviceInfoStruct4Watch?.Clear();
                    receivedDeviceInfoStruct4Watch = null;

                    Log.Error(e, $"In {location}: {e.Message} Rewatch.");

                    Watch4MainDevice();

                    break;
                }
            }
        }).Start();
    }

    /// <summary>
    /// 观察结束
    /// </summary>
    internal static async void WatchingOver(bool foundMainDevice, string serverAddress, int serverPort)
    {
        var location = $"{nameof(DevicesNetwork)}.{nameof(WatchingOver)}";

        Log.Information($"In {location}: " +
            $"{nameof(foundMainDevice)} -> {foundMainDevice}, " +
            $"{nameof(serverAddress)} -> {serverAddress}, " +
            $"{nameof(serverPort)} -> {serverPort}");

        if (foundMainDevice)
        {
            devicesClient ??= new();

            await devicesClient
                .SetServerAddress(serverAddress)
                .SetServerPort(serverPort)
                .Start()
                ;
        }
        else
        {
            devicesServer?.Start();
        }
    }

    public static void Start()
    {
        devicesServer = new();

        devicesClient = new();

        InitEvents();

        KeepCheckAndRemove();

        Watch4MainDevice();
    }

    public static void Stop()
    {
        devicesServer?.Stop();

        devicesClient?.Stop();
    }

    public static void Restart()
    {
        Stop();

        Start();
    }
}

//
//                                        ___-------___
//                                    _-~~             ~~-_
//                                 _-~                    /~-_
//              /^\__/^\         /~  \                   /    \
//            /|  O|| O|        /      \_______________/        \
//           | |___||__|      /       /                \          \
//           |          \    /      /                    \          \
//           |   (_______) /______/                        \_________ \
//           |         / /         \                      /            \
//            \         \^\         \                  /               \     /
//              \         ||           \______________/      _-_       //\__//
//                \       ||------_-~~-_ ------------- \ --/~   ~\    || __/
//                  ~-----||====/~     |==================|       |/~~~~~
//                   (_(__/  ./     /                    \_\      \.
//                          (_(___/                         \_____)_)-jurcy
// 
//
