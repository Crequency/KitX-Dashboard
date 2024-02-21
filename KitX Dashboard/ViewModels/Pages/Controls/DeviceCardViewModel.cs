using Avalonia;
using Common.BasicHelper.Utils.Extensions;
using KitX.Shared.Device;
using Material.Icons;
using System.ComponentModel;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class DeviceCardViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public DeviceCardViewModel()
    {

    }

    private DeviceInfo deviceInfo = new();

    internal DeviceInfo DeviceInfo
    {
        get => deviceInfo;
        set
        {
            deviceInfo = value;
            DeviceName = DeviceInfo.Device.DeviceName;
            DeviceMacAddress = DeviceInfo.Device.MacAddress.IsNullOrWhiteSpace()
                ?
                Translate("Text_Device_NoMacAddress")
                :
                DeviceInfo.Device.MacAddress
                ;
            LastOnlineTime = DeviceInfo.SendTime.ToLocalTime().ToString("yyyy.MM.dd HH:mm:ss");
            DeviceVersion = DeviceInfo.DeviceOSVersion;
            DeviceOSKind = DeviceInfo.DeviceOSType switch
            {
                OperatingSystems.Unknown => MaterialIconKind.QuestionMarkCircle,
                OperatingSystems.Android => MaterialIconKind.Android,
                OperatingSystems.Browser => MaterialIconKind.MicrosoftEdge,
                OperatingSystems.FreeBSD => MaterialIconKind.Freebsd,
                OperatingSystems.IOS => MaterialIconKind.AppleIos,
                OperatingSystems.Linux => MaterialIconKind.Linux,
                OperatingSystems.MacCatalyst => MaterialIconKind.Apple,
                OperatingSystems.MacOS => MaterialIconKind.AppleKeyboardCommand,
                OperatingSystems.TvOS => MaterialIconKind.Apple,
                OperatingSystems.WatchOS => MaterialIconKind.Apple,
                OperatingSystems.Windows => MaterialIconKind.MicrosoftWindows,
                OperatingSystems.IoT => MaterialIconKind.Chip,
                _ => MaterialIconKind.QuestionMarkCircle,
            };
            IPv4 = $"{DeviceInfo.Device.IPv4}:{DeviceInfo.PluginsServerPort}";
            IPv6 = DeviceInfo.Device.IPv6;
            PluginsCount = DeviceInfo.PluginsCount.ToString();
            DeviceControlStatus = DeviceInfo.IsMainDevice
                ? Translate("Text_Device_Type_Master")
                : Translate("Text_Device_Type_Slave");

            PropertyChanged?.Invoke(
                this,
                new(nameof(DeviceName))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(DeviceMacAddress))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(LastOnlineTime))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(DeviceVersion))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(DeviceOSKind))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(IPv4))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(IPv6))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(PluginsCount))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(DeviceControlStatus))
            );
            PropertyChanged?.Invoke(
                this,
                new(nameof(DeviceServerAddress))
            );
        }
    }

    internal string? DeviceName { get; set; }

    internal string? DeviceMacAddress { get; set; }

    internal string? LastOnlineTime { get; set; }

    internal string? DeviceVersion { get; set; }

    internal MaterialIconKind? DeviceOSKind { get; set; }

    internal string? IPv4 { get; set; }

    internal string? IPv6 { get; set; }

    internal string? PluginsCount { get; set; }

    internal string? DeviceControlStatus { get; set; }

    internal string? DeviceServerAddress
    {
        get => deviceInfo.IsMainDevice
            ? $"{deviceInfo.Device.IPv4}:{deviceInfo.DevicesServerPort}"
            : null;
    }

    public override void InitCommands() => throw new System.NotImplementedException();

    public override void InitEvents() => throw new System.NotImplementedException();
}
