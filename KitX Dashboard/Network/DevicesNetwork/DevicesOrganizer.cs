﻿using KitX.Dashboard.Configuration;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Device;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Timer = System.Timers.Timer;

namespace KitX.Dashboard.Network.DevicesNetwork;

internal class DevicesOrganizer : ConfigFetcher
{
    private static readonly object _receivedDeviceInfo4WatchLock = new();

    internal static List<DeviceInfo>? receivedDeviceInfo4Watch;

    internal static readonly Queue<DeviceInfo> deviceInfoStructs = new();

    private static readonly object AddDeviceCard2ViewLock = new();

    private static bool KeepCheckAndRemoveTaskRunning = false;

    public static void Start()
    {
        InitEvents();

        KeepCheckAndRemove();

        Watch4MainDevice();
    }

    private static void InitEvents()
    {
        EventService.OnReceivingDeviceInfo += deviceInfo =>
        {
            deviceInfoStructs.Enqueue(deviceInfo);

            lock (_receivedDeviceInfo4WatchLock)
            {
                receivedDeviceInfo4Watch?.Add(deviceInfo);
            }

            if (deviceInfo.IsMainDevice && deviceInfo.DevicesServerBuildTime < ConstantTable.ServerBuildTime)
            {
                ConstantTable.IsMainMachine = false;

                Watch4MainDevice();

                Log.Information(
                    new StringBuilder()
                        .AppendLine("Watched earlier built server.")
                        .AppendLine($"DevicesServerAddress: {deviceInfo.Device.IPv4}:{deviceInfo.DevicesServerPort} ")
                        .AppendLine($"DevicesServerBuildTime: {deviceInfo.DevicesServerBuildTime}")
                        .ToString()
                );
            }
        };
    }

    private static void UpdateSourceAndAddCards()
    {
        var thisTurnAdded = new List<int>();

        while (deviceInfoStructs.Count > 0)
        {
            var deviceInfoStruct = deviceInfoStructs.Dequeue();

            var hashCode = deviceInfoStruct.GetHashCode();

            var findThis = thisTurnAdded.Contains(hashCode);

            if (findThis) continue;

            foreach (var item in ViewInstances.DeviceCases)
            {
                if (item.DeviceInfo.IsSameDevice(deviceInfoStruct))
                {
                    item.DeviceInfo = deviceInfoStruct;
                    findThis = true;
                    break;
                }
            }

            if (!findThis)
            {
                thisTurnAdded.Add(hashCode);

                ViewInstances.DeviceCases.Add(new(deviceInfoStruct));
            }
        }
    }

    private static void RemoveOfflineCards()
    {
        var devicesNeedToBeRemoved = new List<DeviceCase>();

        foreach (var item in ViewInstances.DeviceCases)
            if (item.DeviceInfo.IsOffline())
                devicesNeedToBeRemoved.Add(item);

        foreach (var item in devicesNeedToBeRemoved)
            ViewInstances.DeviceCases.Remove(item);
    }

    private static void MoveSelfCardToFirst()
    {
        var index = 0;

        foreach (var item in ViewInstances.DeviceCases)
        {
            if (item.DeviceInfo.IsCurrentDevice())
            {
                if (index != 0)
                    ViewInstances.DeviceCases.Move(index, 0);

                break;
            }

            ++index;
        }
    }

    private static void KeepCheckAndRemove()
    {
        var location = $"{nameof(DevicesOrganizer)}.{nameof(KeepCheckAndRemove)}";

        var timer = new Timer()
        {
            Interval = AppConfig.Web.DevicesViewRefreshDelay,
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

                    if (AppConfig.Web.DisableRemovingOfflineDeviceCard == false)
                        RemoveOfflineCards();

                    MoveSelfCardToFirst();

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
            timer.Interval = AppConfig.Web.DevicesViewRefreshDelay;
        };
    }

    internal static void Watch4MainDevice(CancellationToken token = default)
    {
        var location = $"{nameof(DevicesOrganizer)}.{nameof(Watch4MainDevice)}";

        new Thread(() =>
        {
            receivedDeviceInfo4Watch = [];

            var checkedTime = 0;
            var hadMainDevice = false;
            var earliestBuiltServerTime = DateTime.UtcNow;
            var serverPort = 0;
            var serverAddress = string.Empty;

            while (checkedTime < 7 && token.IsCancellationRequested == false)
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
                                if (item.DevicesServerBuildTime.ToUniversalTime() < earliestBuiltServerTime)
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

                        if (token.IsCancellationRequested == false)
                            WatchingOver(hadMainDevice, serverAddress, serverPort);
                    }

                    Thread.Sleep(1 * 1000); //  Sleep 1 second.
                }
                catch (Exception e)
                {
                    receivedDeviceInfo4Watch?.Clear();
                    receivedDeviceInfo4Watch = null;

                    Log.Error(e, $"In {location}: {e.Message} Rewatch.");

                    if (token.IsCancellationRequested == false)
                        Watch4MainDevice();

                    break;
                }
            }
        }).Start();
    }

    private static void WatchingOver(bool foundMainDevice, string serverAddress, int serverPort)
    {
        var location = $"{nameof(DevicesOrganizer)}.{nameof(WatchingOver)}";

        Log.Information(
            new StringBuilder()
                .Append($"In {location}: ")
                .Append($"{nameof(foundMainDevice)} -> {foundMainDevice}")
                .Append(", ")
                .Append($"{nameof(serverAddress)} -> {serverAddress}")
                .Append(", ")
                .Append($"{nameof(serverPort)} -> {serverPort}")
                .ToString()
        );

        if (foundMainDevice)
        {
            ConstantTable.MainMachineAddress = serverAddress;
            ConstantTable.MainMachinePort = serverPort;
        }
        else
        {
            ConstantTable.IsMainMachine = true;
        }
    }
}
