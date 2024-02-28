using KitX.Shared.CSharp.Device;
using ReactiveUI;

namespace KitX.Dashboard.Models;

public class DeviceCase(DeviceInfo deviceInfo) : ReactiveObject
{
    private DeviceInfo deviceInfo = deviceInfo;

    public DeviceInfo DeviceInfo
    {
        get => deviceInfo;
        set => this.RaiseAndSetIfChanged(ref deviceInfo, value);
    }
}
