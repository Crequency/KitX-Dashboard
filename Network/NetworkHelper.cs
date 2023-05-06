using Common.BasicHelper.Core.Shell;
using Common.BasicHelper.Utils.Extensions;
using KitX.Web.Rules;
using KitX_Dashboard.Converters;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace KitX_Dashboard.Network;

internal static class NetworkHelper
{
    /// <summary>
    /// 检查网络适配器是否符合要求
    /// </summary>
    /// <param name="adapter">网络适配器</param>
    /// <returns>是否符合要求</returns>
    internal static bool CheckNetworkInterface
    (
        NetworkInterface adapter,
        IPInterfaceProperties adapterProperties
    )
    {
        var userPointed = ConfigManager.AppConfig.Web.AcceptedNetworkInterfaces;
        if (userPointed is not null)
            if (userPointed.Contains(adapter.Name))
                return true;
            else return false;

        if (adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
            adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211
            &&
            (
                adapterProperties.MulticastAddresses.Count == 0 ||
                // most of VPN adapters will be skipped
                !adapter.SupportsMulticast ||
                // multicast is meaningless for this type of connection
                OperationalStatus.Up != adapter.OperationalStatus ||
                // this adapter is off or not connected
                !adapter.Supports(NetworkInterfaceComponent.IPv4)
            )
            ) return false;
        return true;
    }

    /// <summary>
    /// 将 IPv4 的十进制表示按点分制拆分
    /// </summary>
    /// <param name="ip">IPv4 的十进制表示</param>
    /// <returns>拆分</returns>
    internal static (int, int, int, int) IPv4_2_4Parts(string ip)
    {
        string[] p = ip.Split('.');
        int a = int.Parse(p[0]), b = int.Parse(p[1]), c = int.Parse(p[2]), d = int.Parse(p[3]);
        return (a, b, c, d);
    }

    /// <summary>
    /// 获取本机内网 IPv4 地址
    /// </summary>
    /// <returns>使用点分十进制表示法的本机内网IPv4地址</returns>
    internal static string GetInterNetworkIPv4()
    {
        try
        {
            return (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    where ip.AddressFamily == AddressFamily.InterNetwork
                        && !ip.ToString().Equals("127.0.0.1")
                        && (ip.ToString().StartsWith("192.168")                         //  192.168.x.x
                            || ip.ToString().StartsWith("10")                           //  10.x.x.x
                            || IPv4_2_4Parts(ip.ToString()).Item1 == 172               //  172.16-31.x.x
                                && IPv4_2_4Parts(ip.ToString()).Item2 >= 16
                                && IPv4_2_4Parts(ip.ToString()).Item2 <= 31)
                        && ip.ToString().StartsWith(ConfigManager.AppConfig.Web.IPFilter)  //  满足自定义规则
                    select ip).First().ToString();
        }
        catch (Exception ex)
        {
            var location = $"{nameof(NetworkHelper)}.{nameof(GetInterNetworkIPv4)}";
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 获取本机内网 IPv6 地址
    /// </summary>
    /// <returns>IPv6 地址</returns>
    internal static string GetInterNetworkIPv6()
    {
        try
        {
            return (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    where ip.AddressFamily == AddressFamily.InterNetworkV6
                        && !ip.ToString().Equals("::1")
                    select ip).First().ToString();
        }
        catch (Exception ex)
        {
            var location = $"{nameof(NetworkHelper)}.{nameof(GetInterNetworkIPv6)}";
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 尝试获取设备 MAC 地址
    /// </summary>
    /// <returns>MAC 地址</returns>
    internal static string? TryGetDeviceMacAddress()
    {
        try
        {
            var mac = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(nic => nic.GetPhysicalAddress().ToString()).FirstOrDefault();

            return mac?.SeparateGroup(2, sb => sb.Append(':'));
        }
        catch (Exception ex)
        {
            var location = $"{nameof(NetworkHelper)}.{nameof(TryGetDeviceMacAddress)}";
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 尝试获取系统版本
    /// </summary>
    /// <returns>系统版本</returns>
    internal static string? TryGetOSVersionString()
    {
        var result = Environment.OSVersion.VersionString;
        try
        {
            switch (OperatingSystem2Enum.GetOSType())
            {
                case OperatingSystems.Linux:

                    var versionFilePath = "/etc/os-release";
                    var versionSegament = "PRETTY_NAME";
                    var needFindInIssue = false;

                    if (File.Exists(versionFilePath))
                    {
                        var osRelease = File.ReadAllLines(versionFilePath)
                            .Select(line => line.Split('='))
                            .ToDictionary
                            (
                                parts => parts[0],
                                parts => parts[1].Trim('"')
                            );

                        if (osRelease.TryGetValue(versionSegament, out var version))
                            result = version;
                        else needFindInIssue = true;
                    }

                    if (needFindInIssue)
                    {
                        var issueFilePath = "/etc/issue";
                        if (File.Exists(issueFilePath))
                        {
                            var issue = File.ReadAllText(issueFilePath);
                            var lines = issue.Split('\n');
                            result = lines.First(x => !x.Equals(string.Empty));
                        }
                    }

                    break;
                case OperatingSystems.MacOS:
                    var command = "sw_vers";

                    var productName = command.ExecuteAsCommand("-productName");
                    var productVersion = command.ExecuteAsCommand("-productVersion");
                    var buildVersion = command.ExecuteAsCommand("-buildVersion");

                    if (productName is not null && productVersion is not null && buildVersion is not null)
                        result = $"{productName} {productVersion} {buildVersion}"
                            .Replace("\n", "");

                    break;
            }
        }
        catch (Exception ex)
        {
            var location = $"{nameof(NetworkHelper)}.{nameof(TryGetOSVersionString)}";
            Log.Error(ex, $"In {location}: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// 获取设备信息
    /// </summary>
    /// <returns>设备信息结构体</returns>
    internal static DeviceInfoStruct GetDeviceInfo() => new()
    {
        DeviceName = Environment.MachineName,
        DeviceMacAddress = TryGetDeviceMacAddress(),
        IsMainDevice = GlobalInfo.IsMainMachine,
        SendTime = DateTime.UtcNow,
        DeviceOSType = OperatingSystem2Enum.GetOSType(),
        DeviceOSVersion = TryGetOSVersionString(),
        IPv4 = GetInterNetworkIPv4(),
        IPv6 = GetInterNetworkIPv6(),
        PluginServerPort = GlobalInfo.PluginServerPort,
        DeviceServerPort = GlobalInfo.DeviceServerPort,
        DeviceServerBuildTime = new(),
        PluginsCount = Program.PluginCards.Count,
    };
}
