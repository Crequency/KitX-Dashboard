using Avalonia.Threading;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using KitX.Shared.Device;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using Timer = System.Timers.Timer;

namespace KitX.Dashboard.Network;

internal class DevicesNetwork
{
    internal static DevicesServer? devicesServer = null;

    internal static DevicesClient? devicesClient = null;

    private static readonly object _receivedDeviceInfo4WatchLock = new();

    internal static List<DeviceInfo>? receivedDeviceInfo4Watch;

    internal static readonly Queue<DeviceInfo> deviceInfoStructs = new();

    private static readonly object AddDeviceCard2ViewLock = new();

    private static bool KeepCheckAndRemoveTaskRunning = false;

    private static void InitEvents()
    {
        EventService.OnReceivingDeviceInfo += dis =>
        {
            if (dis.IsMainDevice && dis.DeviceServerBuildTime < ConstantTable.ServerBuildTime)
            {
                Stop();

                Watch4MainDevice();

                Log.Information($"In DevicesService: Watched earlier built server. " +
                    $"DeviceServerAddress: {dis.Device.IPv4}:{dis.DevicesServerPort} " +
                    $"DeviceServerBuildTime: {dis.DeviceServerBuildTime}");
            }
        };
    }

    private static bool CheckDeviceIsOffline(DeviceInfo info)
        => DateTime.UtcNow - info.SendTime.ToUniversalTime() > new TimeSpan(
            0,
            0,
            Instances.ConfigManager.AppConfig.Web.DeviceInfoTTLSeconds
        );

    private static bool CheckIsCurrentMachine(DeviceInfo info)
    {
        var self = DevicesDiscoveryServer.DefaultDeviceInfo;

        return info.Device.MacAddress.Equals(self.Device.MacAddress)
            && info.Device.DeviceName.Equals(self.Device.DeviceName);
    }

    private static void UpdateSourceAndAddCards()
    {
        var needToAddDevicesCount = 0;

        var thisTurnAdded = new List<int>();

        while (deviceInfoStructs.Count > 0)
        {
            var deviceInfoStruct = deviceInfoStructs.Dequeue();

            var hashCode = deviceInfoStruct.GetHashCode();

            var findThis = thisTurnAdded.Contains(hashCode);

            if (findThis) continue;

            foreach (var item in ViewInstances.DeviceCards)
            {
                if (item.viewModel.DeviceInfo.Device.DeviceName.Equals(deviceInfoStruct.Device.DeviceName))
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
                    ++needToAddDevicesCount;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    lock (AddDeviceCard2ViewLock)
                    {
                        ViewInstances.DeviceCards.Add(new(deviceInfoStruct));

                        --needToAddDevicesCount;
                    }
                });
            }
        }

        while (needToAddDevicesCount != 0) ;
    }

    private static void RemoveOfflineCards()
    {
        var devicesNeedToBeRemoved = new List<DeviceCard>();

        foreach (var item in ViewInstances.DeviceCards)
        {
            var info = item.viewModel.DeviceInfo;

            if (CheckDeviceIsOffline(info)) devicesNeedToBeRemoved.Add(item);
        }

        var removeDeviceTaskRunning = true;

        Dispatcher.UIThread.Post(() =>
        {
            lock (AddDeviceCard2ViewLock)
            {
                foreach (var item in devicesNeedToBeRemoved)
                    ViewInstances.DeviceCards.Remove(item);
            }
            removeDeviceTaskRunning = false;
        });

        while (removeDeviceTaskRunning) ;
    }

    private static void MoveSelfCard2First()
    {
        var index = 0;
        var moveSelfCardTaskRunning = true;

        foreach (var item in ViewInstances.DeviceCards)
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
                            ViewInstances.DeviceCards.Move(index, 0);
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

    private static void KeepCheckAndRemove()
    {
        var location = $"{nameof(DevicesNetwork)}.{nameof(KeepCheckAndRemove)}";

        var timer = new Timer()
        {
            Interval = Instances.ConfigManager.AppConfig.Web.DevicesViewRefreshDelay,
            AutoReset = true
        };

        timer.Elapsed += (_, _) =>
        {
            try
            {
                if (KeepCheckAndRemoveTaskRunning)
                    Log.Information($"In {location}: Timer elapsed and skip task.");
                else
                {
                    KeepCheckAndRemoveTaskRunning = true;

                    UpdateSourceAndAddCards();

                    if (!Instances.ConfigManager.AppConfig.Web.DisableRemovingOfflineDeviceCard)
                        RemoveOfflineCards();

                    MoveSelfCard2First();

                    KeepCheckAndRemoveTaskRunning = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        };

        timer.Start();

        EventService.AppConfigChanged += () =>
        {
            timer.Interval = Instances.ConfigManager.AppConfig.Web.DevicesViewRefreshDelay;
        };
    }

    internal static void Update(DeviceInfo deviceInfo)
    {
        deviceInfoStructs.Enqueue(deviceInfo);

        if (receivedDeviceInfo4Watch is not null)
        {
            lock (_receivedDeviceInfo4WatchLock)
            {
                receivedDeviceInfo4Watch.Add(deviceInfo);
            }
        }

        EventService.Invoke(nameof(EventService.OnReceivingDeviceInfo), deviceInfo);
    }

    internal static void Watch4MainDevice()
    {
        var location = $"{nameof(DevicesNetwork)}.{nameof(Watch4MainDevice)}";

        new Thread(() =>
        {
            receivedDeviceInfo4Watch = [];

            var checkedTime = 0;
            var hadMainDevice = false;
            var earliestBuiltServerTime = DateTime.UtcNow;
            var serverPort = 0;
            var serverAddress = string.Empty;

            while (checkedTime < 7)
            {
                try
                {
                    if (receivedDeviceInfo4Watch is null) continue;

                    lock (_receivedDeviceInfo4WatchLock)
                    {
                        foreach (var item in receivedDeviceInfo4Watch)
                        {
                            if (item.IsMainDevice)
                            {
                                if (item.DeviceServerBuildTime.ToUniversalTime() < earliestBuiltServerTime)
                                {
                                    serverPort = item.DevicesServerPort;
                                    serverAddress = item.Device.IPv4;
                                }
                                hadMainDevice = true;
                            }
                        }
                    }

                    ++checkedTime;

                    Log.Information($"In {location}: Watched for {checkedTime} times.");

                    if (checkedTime == 7)
                    {
                        receivedDeviceInfo4Watch?.Clear();
                        receivedDeviceInfo4Watch = null;

                        WatchingOver(hadMainDevice, serverAddress, serverPort);
                    }

                    Thread.Sleep(1 * 1000); //  Sleep 1 second.
                }
                catch (Exception e)
                {
                    receivedDeviceInfo4Watch?.Clear();
                    receivedDeviceInfo4Watch = null;

                    Log.Error(e, $"In {location}: {e.Message} Rewatch.");

                    Watch4MainDevice();

                    break;
                }
            }
        }).Start();
    }

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
