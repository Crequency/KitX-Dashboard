using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.Views.Pages.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using Timer = System.Timers.Timer;

namespace KitX_Dashboard.Services
{
    internal class DevicesManager
    {
        internal static List<DeviceInfoStruct>? receivedDeviceInfoStruct4Watch;

        internal static readonly Queue<DeviceInfoStruct> deviceInfoStructs = new();

        private static readonly object AddDeviceCard2ViewLock = new();

        /// <summary>
        /// 持续检查并移除
        /// </summary>
        internal static void KeepCheckAndRemove()
        {
            Timer timer = new()
            {
                Interval = 1000,
                AutoReset = true
            };
            timer.Elapsed += (_, _) =>
            {
                while (deviceInfoStructs.Count > 0)
                {
                    DeviceInfoStruct deviceInfoStruct = deviceInfoStructs.Dequeue();
                    bool findThis = false;
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
                        Dispatcher.UIThread.Post(() =>
                        {
                            lock (AddDeviceCard2ViewLock)
                            {
                                Program.DeviceCards.Add(new(deviceInfoStruct));
                            }
                        });
                    }
                }

                List<string> MacAddressVisited = new();
                List<string> IPv4AddressVisited = new();
                List<string> IPv6AddressVisited = new();
                List<DeviceCard> DevicesNeed2BeRemoved = new();

                foreach (var item in Program.DeviceCards)
                {
                    if (MacAddressVisited.Contains(item.viewModel.DeviceInfo.DeviceMacAddress))
                    {
                        DevicesNeed2BeRemoved.Add(item);
                        continue;
                    }
                    if (IPv4AddressVisited.Contains(item.viewModel.DeviceInfo.IPv4))
                    {
                        DevicesNeed2BeRemoved.Add(item);
                        continue;
                    }
                    if (IPv6AddressVisited.Contains(item.viewModel.DeviceInfo.IPv6))
                    {
                        DevicesNeed2BeRemoved.Add(item);
                        continue;
                    }
                    MacAddressVisited.Add(item.viewModel.DeviceInfo.DeviceMacAddress);
                    IPv4AddressVisited.Add(item.viewModel.DeviceInfo.IPv4);
                    IPv6AddressVisited.Add(item.viewModel.DeviceInfo.IPv6);
                    if (DateTime.Now - item.viewModel.DeviceInfo.SendTime
                        > new TimeSpan(0, 0, Program.Config.Web.DeviceInfoStructTTLSeconds))
                        DevicesNeed2BeRemoved.Add(item);
                }
                foreach (var item in DevicesNeed2BeRemoved)
                {
                    lock (AddDeviceCard2ViewLock)
                    {
                        Program.DeviceCards.Remove(item);
                    }
                }

                int index = 0;
                DeviceCard? localMachine = null;
                foreach (var item in Program.DeviceCards)
                {
                    if (item.viewModel.DeviceInfo.DeviceMacAddress.Equals(
                            DevicesServer.DefaultDeviceInfoStruct.DeviceMacAddress)
                        && item.viewModel.DeviceInfo.DeviceName.Equals(
                            DevicesServer.DefaultDeviceInfoStruct.DeviceName))
                    {
                        localMachine = item;
                        break;
                    }
                    ++index;
                }
                if (index != 0)
                {
                    try
                    {
                        Program.DeviceCards.Move(index, 0);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Can't move self 2 first. {e.Message}", e);
                    }
                }
            };
            timer.Start();
        }

        /// <summary>
        /// 更新收到的UDP包
        /// </summary>
        /// <param name="deviceInfo">设备信息结构</param>
        internal static void Update(DeviceInfoStruct deviceInfo)
        {
            deviceInfoStructs.Enqueue(deviceInfo);
            receivedDeviceInfoStruct4Watch?.Add(deviceInfo);

            if (deviceInfo.IsMainDevice)
                EventHandlers.Invoke(nameof(EventHandlers.OnReceivingDeviceInfoStruct4DeviceNet),
                    deviceInfo);
        }

        /// <summary>
        /// 观察主控
        /// </summary>
        internal static void Watch4MainDevice()
        {
            new Thread(() =>
            {
                try
                {
                    receivedDeviceInfoStruct4Watch = new();
                    int checkedTime = 0;
                    bool hadMainDevice = false;
                    DateTime earliestBuiltServerTime = DateTime.Now;
                    int serverPort = 0;
                    string serverAddress = string.Empty;
                    Timer timer = new()
                    {
                        Interval = 1000,
                        AutoReset = true
                    };
                    timer.Elapsed += (_, _) =>
                    {
                        try
                        {
                            foreach (var item in receivedDeviceInfoStruct4Watch)
                            {
                                if (item.IsMainDevice)
                                {
                                    if (item.DeviceServerBuildTime < earliestBuiltServerTime)
                                    {
                                        serverPort = item.DeviceServerPort;
                                        serverAddress = item.IPv4;
                                    }
                                    hadMainDevice = true;
                                }
                            }
                            ++checkedTime;
                            if (checkedTime == 7)
                            {
                                timer.Stop();
                                receivedDeviceInfoStruct4Watch?.Clear();
                                receivedDeviceInfoStruct4Watch = null;
                                WatchingOver(hadMainDevice, serverAddress, serverPort);
                            }
                        }
                        catch (Exception e)
                        {
                            receivedDeviceInfoStruct4Watch?.Clear();
                            receivedDeviceInfoStruct4Watch = null;
                            Log.Error("In Watch4MainDevice", e);
                        }
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    receivedDeviceInfoStruct4Watch?.Clear();
                    receivedDeviceInfoStruct4Watch = null;
                    Log.Error("In Watch4MainDevice", ex);
                }
            }).Start();
        }

        /// <summary>
        /// 观察结束
        /// </summary>
        internal static void WatchingOver(bool hadMainDevice, string serverAddress, int serverPort)
        {
            Log.Information($"In WatchingOver: hadMainDevice: {hadMainDevice}, " +
                $"serverAddress: {serverAddress}, serverPort: {serverPort}");
            if (hadMainDevice)
            {
                Program.WebManager?.devicesServer?.AttendServer(serverAddress, serverPort);
            }
            else
            {
                Program.WebManager?.devicesServer?.BuildServer();
            }
        }
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
