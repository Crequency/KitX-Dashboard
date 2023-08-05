using Common.BasicHelper.Core.Shell;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Converters;
using KitX.Dashboard.Data;
using KitX.Dashboard.Managers;
using KitX.Web.Rules;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace KitX.Dashboard.Network;

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
    /// 判断 IPv4 地址是否为内部网络地址
    /// </summary>
    /// <param name="address">网络地址</param>
    /// <returns>是否为内部网络地址</returns>
    internal static bool IsInterNetworkAddressV4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        return bytes[0] switch
        {
            10 => true,
            172 => bytes[1] <= 31 && bytes[1] >= 16,
            192 => bytes[1] == 168,
            _ => false,
        };
    }

    /// <summary>
    /// 获取本机内网 IPv4 地址
    /// </summary>
    /// <returns>使用点分十进制表示法的本机内网IPv4地址</returns>
    internal static string GetInterNetworkIPv4()
    {
        var location = $"{nameof(NetworkHelper)}.{nameof(GetInterNetworkIPv4)}";

        try
        {
            var search =
                from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                where ip.AddressFamily == AddressFamily.InterNetwork
                    && IsInterNetworkAddressV4(ip)
                    && !ip.ToString().Equals("127.0.0.1")
                    && ip.ToString().StartsWith(ConfigManager.AppConfig.Web.IPFilter)
                select ip;

            Log.Information($"IPv4 addresses: {search.Print(print: false)}");

            var result = search.FirstOrDefault()?.ToString();

            return result ?? string.Empty;
        }
        catch (Exception ex)
        {
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
        var location = $"{nameof(NetworkHelper)}.{nameof(GetInterNetworkIPv6)}";

        try
        {
            var search =
                from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                where ip.AddressFamily == AddressFamily.InterNetworkV6
                    && !ip.ToString().Equals("::1")
                select ip;

            Log.Information($"IPv6 addresses: {search.Print(print: false)}");

            var result = search.FirstOrDefault()?.ToString();

            return result ?? string.Empty;
        }
        catch (Exception ex)
        {
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
        var location = $"{nameof(NetworkHelper)}.{nameof(TryGetDeviceMacAddress)}";

        try
        {
            var mac =
                from nic in NetworkInterface.GetAllNetworkInterfaces()
                where nic.OperationalStatus == OperationalStatus.Up
                  && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                select nic.GetPhysicalAddress().ToString();

            var result = mac.FirstOrDefault()?.SeparateGroup(
                2, sb => sb.Append(':')
            );

            return result;
        }
        catch (Exception ex)
        {
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
        var location = $"{nameof(NetworkHelper)}.{nameof(TryGetOSVersionString)}";

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

                    if (productName is null || productVersion is null || buildVersion is null)
                        break;

                    result = $"{productName} {productVersion} {buildVersion}"
                        .Replace("\n", "");

                    break;
            }
        }
        catch (Exception ex)
        {
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
        PluginsCount = Instances.PluginCards.Count,
    };
}
