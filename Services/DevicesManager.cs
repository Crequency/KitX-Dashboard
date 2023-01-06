using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Views.Pages.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace KitX_Dashboard.Services
{
    internal class DevicesManager
    {
        internal static void InitEvents()
        {
            EventHandlers.OnReceivingDeviceInfoStruct4DeviceNet += dis =>
            {
                if (GlobalInfo.IsMainMachine)
                    if (dis.DeviceServerBuildTime < GlobalInfo.ServerBuildTime)
                    {
                        Program.WebManager?.devicesServer?.CancleBuildServer();
                        Log.Information($"In DevicesManager: Watched for earlier built server. " +
                            $"DeviceServerAddress: {dis.IPv4}:{dis.DeviceServerPort} " +
                            $"DeviceServerBuildTime: {dis.DeviceServerBuildTime}");
                    }
            };
        }

        internal static List<DeviceInfoStruct>? receivedDeviceInfoStruct4Watch;

        internal static readonly Queue<DeviceInfoStruct> deviceInfoStructs = new();

        private static readonly object AddDeviceCard2ViewLock = new();

        private static bool KeepCheckAndRemoveTaskRunning = false;

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
                try
                {
                    var location = $"{nameof(DevicesServer)}.{nameof(KeepCheckAndRemove)}()";
                    if (KeepCheckAndRemoveTaskRunning)
                        Log.Information($"In {location}: Timer elapsed and skip task.");
                    else
                    {
                        Log.Information($"In {location}: Timer elapsed and run task.");

                        KeepCheckAndRemoveTaskRunning = true;

                        #region 更新已有卡片的信息, 添加不存在卡片到界面

                        while (deviceInfoStructs.Count > 0)
                        {
                            var deviceInfoStruct = deviceInfoStructs.Dequeue();
                            var findThis = false;
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

                        #endregion

                        #region 查找重复的卡片并移除

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
                            if (DateTime.UtcNow - info.SendTime.ToUniversalTime()
                                > new TimeSpan(0, 0,
                                    Program.Config.Web.DeviceInfoStructTTLSeconds))
                                DevicesNeed2BeRemoved.Add(item);
                        }
                        foreach (var item in DevicesNeed2BeRemoved)
                        {
                            lock (AddDeviceCard2ViewLock)
                            {
                                Program.DeviceCards.Remove(item);
                            }
                        }

                        #endregion

                        #region 寻找本机卡片并移到最前面

                        int index = 0;
                        DeviceCard? localMachine = null;
                        foreach (var item in Program.DeviceCards)
                        {
                            var info = item.viewModel.DeviceInfo;
                            var self = DevicesServer.DefaultDeviceInfoStruct;
                            if (info.DeviceMacAddress.Equals(self.DeviceMacAddress)
                                && info.DeviceName.Equals(self.DeviceName))
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
                                Log.Warning(e, $"Can't move self 2 first. {e.Message}");
                            }
                        }

                        #endregion

                        KeepCheckAndRemoveTaskRunning = false;
                    }
                }
                catch (Exception ex)
                {
                    var location = $"{nameof(DevicesManager)}.{nameof(KeepCheckAndRemove)}()";
                    Log.Error(ex, $"In {location}: {ex.Message}");
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
                            foreach (var item in receivedDeviceInfoStruct4Watch)
                            {
                                if (item.IsMainDevice)
                                {
                                    if (item.DeviceServerBuildTime.ToUniversalTime()
                                        < earliestBuiltServerTime)
                                    {
                                        serverPort = item.DeviceServerPort;
                                        serverAddress = item.IPv4;
                                    }
                                    hadMainDevice = true;
                                }
                            }
                            ++checkedTime;
                            Log.Information($"In Watch4MainDevice: " +
                                $"Watched for {checkedTime} times.");
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
                            Log.Error(e, "In Watch4MainDevice");
                        }
                    }
                }
                catch (Exception ex)
                {
                    receivedDeviceInfoStruct4Watch?.Clear();
                    receivedDeviceInfoStruct4Watch = null;
                    Log.Error(ex, "In Watch4MainDevice");
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
