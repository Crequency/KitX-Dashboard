using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Security;
using KitX.Core.Device;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Device;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class DevicesPageViewModel : ViewModelBase, IDisposable
{
    private readonly IDeviceDiscoveryService _discoveryService;
    private readonly INetworkService _networkService;
    private readonly IConfigService _configService;
    private readonly IDeviceKeyService _securityService;
    private readonly IDeviceServer _devicesServer;

    public DevicesPageViewModel(
        IDeviceDiscoveryService discoveryService,
        INetworkService networkService,
        IConfigService configService,
        IDeviceKeyService securityService,
        IDeviceServer devicesServer)
    {
        _discoveryService = discoveryService;
        _networkService = networkService;
        _configService = configService;
        _securityService = securityService;
        _devicesServer = devicesServer;

        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        RestartDevicesServerCommand = ReactiveCommand.Create(async () =>
        {
            // Server stop/restart orchestration (including the UDP settle delay)
            // lives in Core's INetworkService; the UI only clears the device list.
            await _networkService.RestartDevicesServersAsync();

            DeviceCases.Clear();
        });

        StopDevicesServerCommand = ReactiveCommand.Create(async () =>
        {
            // Stops the discovery + devices servers only; the plugin server is
            // intentionally left untouched (matches the previous behavior).
            await _networkService.StopDevicesServersAsync();

            DeviceCases.Clear();
        });
    }

    public sealed override void InitEvents()
    {
        // Subscribe to device discovery events (D11: named handler, unsubscribed in Dispose)
        if (_discoveryService is not null)
            _discoveryService.DeviceDiscovered += OnDeviceDiscovered;

        DeviceCases.CollectionChanged += OnDeviceCasesChanged;
    }

    private void OnDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
    {
        if (e.DeviceInfo is null) return;

        // Check if device already exists using IsSameDevice
        var existingDevice = DeviceCases
            .OfType<DeviceCase>()
            .FirstOrDefault(x => x.DeviceInfo.Device.IsSameDevice(e.DeviceInfo.Device));
        if (existingDevice is null)
        {
            // Create the device case with constructor-injected services
            // (DeviceCase requires the runtime DeviceInfo plus DI services).
            var deviceCase = new DeviceCase(e.DeviceInfo, _configService, _securityService, _devicesServer, _discoveryService);
            DeviceCases.Add(deviceCase);
        }
        else
        {
            // Update existing device info
            existingDevice.DeviceInfo = e.DeviceInfo;
        }
    }

    private void OnDeviceCasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyFilter();
        DevicesCount = DeviceCases.Count.ToString();
    }

    /// <summary>
    /// Unsubscribes every subscription made in <see cref="InitEvents"/>. The VM is
    /// DI-transient and recreated on each navigation; without this the singleton
    /// services would accumulate subscriptions for every disposed instance (D11).
    /// </summary>
    public void Dispose()
    {
        if (_discoveryService is not null)
            _discoveryService.DeviceDiscovered -= OnDeviceDiscovered;

        DeviceCases.CollectionChanged -= OnDeviceCasesChanged;
    }

    private string? _searchingText;

    internal string? SearchingText
    {
        get => _searchingText;
        set
        {
            if (_searchingText == value) return;
            _searchingText = value;
            ApplyFilter();
        }
    }

    /// <summary>
    /// Filtered view of <see cref="DeviceCases"/> bound by the page's device grid.
    /// Matches device name / IPv4 / MAC address, ignoring case; empty keyword shows all.
    /// </summary>
    private readonly ObservableCollection<IDeviceCase> _displayedDeviceCases = [];

    internal ObservableCollection<IDeviceCase> DisplayedDeviceCases => _displayedDeviceCases;

    private void ApplyFilter()
    {
        var keyword = _searchingText?.Trim() ?? string.Empty;

        _displayedDeviceCases.Clear();

        foreach (var device in DeviceCases)
        {
            var locator = device.DeviceInfo.Device;

            if (keyword.Length == 0
                || locator.DeviceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || locator.IPv4.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || locator.MacAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                _displayedDeviceCases.Add(device);
        }

        NoDevice_TipHeight = _displayedDeviceCases.Count == 0 ? 300 : 0;
    }

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
