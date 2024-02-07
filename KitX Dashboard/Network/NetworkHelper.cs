using Common.BasicHelper.Core.Shell;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Converters;
using KitX.Dashboard.Data;
using KitX.Dashboard.Managers;
using KitX.Shared.Device;
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

    internal static DeviceInfo GetDeviceInfo() => new()
    {
        Device = new()
        {
            DeviceName = Environment.MachineName,
            MacAddress = TryGetDeviceMacAddress() ?? "",
            IPv4 = GetInterNetworkIPv4(),
            IPv6 = GetInterNetworkIPv6(),
        },
        IsMainDevice = GlobalInfo.IsMainMachine,
        SendTime = DateTime.UtcNow,
        DeviceOSType = OperatingSystem2Enum.GetOSType(),
        DeviceOSVersion = TryGetOSVersionString() ?? "",
        PluginServerPort = GlobalInfo.PluginServerPort,
        DevicesServerPort = GlobalInfo.DevicesServerPort,
        DeviceServerBuildTime = new(),
        PluginsCount = Instances.PluginCards.Count,
    };
}
