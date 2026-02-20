using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Device;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Device;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class DevicesPageViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IDeviceDiscoveryService? _discoveryService;
    private readonly IDeviceServer? _deviceServer;

    public DevicesPageViewModel()
    {
        _configService = ConfigService;

        // Get services from DI
        _discoveryService = App.GetService<IDeviceDiscoveryService>();
        _deviceServer = App.GetService<IDeviceServer>();

        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        RestartDevicesServerCommand = ReactiveCommand.Create(async () =>
        {
            if (_discoveryService is null && _deviceServer is null)
                return;

            // Stop servers
            _deviceServer?.Stop();
            _discoveryService?.Stop();

            await Task.Delay(_configService.AppConfig.Web.UdpSendFrequency + 200);

            DeviceCases.Clear();

            // Restart servers
            _discoveryService?.Run();
            _deviceServer?.Run();
        });

        StopDevicesServerCommand = ReactiveCommand.Create(async () =>
        {
            if (_discoveryService is null && _deviceServer is null)
                return;

            _deviceServer?.Stop();
            _discoveryService?.Stop();

            await Task.Delay(_configService.AppConfig.Web.UdpSendFrequency + 200);

            DeviceCases.Clear();
        });
    }

    public sealed override void InitEvents()
    {
        // Subscribe to device discovery events
        _discoveryService.DeviceDiscovered += (_, e) =>
        {
            if (e.DeviceInfo is null) return;

            // Check if device already exists using IsSameDevice
            var existingDevice = DeviceCases
                .OfType<DeviceCase>()
                .FirstOrDefault(x => x.DeviceInfo.Device.IsSameDevice(e.DeviceInfo.Device));
            if (existingDevice is null)
            {
                // Add new device case
                var deviceCase = new DeviceCase(e.DeviceInfo);
                DeviceCases.Add(deviceCase);
            }
            else
            {
                // Update existing device info
                existingDevice.DeviceInfo = e.DeviceInfo;
            }
        };

        DeviceCases.CollectionChanged += (_, _) =>
        {
            NoDevice_TipHeight = DeviceCases.Count == 0 ? 300 : 0;
            DevicesCount = DeviceCases.Count.ToString();
        };
    }

    internal string? SearchingText { get; set; }

    internal string devicesCount = DeviceCases.Count.ToString();

    internal string DevicesCount
    {
        get => devicesCount;
        set => this.RaiseAndSetIfChanged(ref devicesCount, value);
    }

    internal double noDevice_TipHeight = DeviceCases.Count == 0 ? 300 : 0;

    internal double NoDevice_TipHeight
    {
        get => noDevice_TipHeight;
        set => this.RaiseAndSetIfChanged(ref noDevice_TipHeight, value);
    }

    internal static ObservableCollection<IDeviceCase> DeviceCases => UIStateService.DeviceCases;

    internal ReactiveCommand<Unit, Task>? RestartDevicesServerCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? StopDevicesServerCommand { get; set; }
}
