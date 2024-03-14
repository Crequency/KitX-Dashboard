using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using KitX.Dashboard.Models;
using KitX.Dashboard.Views;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class DevicePageViewModel : ViewModelBase
{
    public DevicePageViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        RestartDevicesServerCommand = ReactiveCommand.Create(async () =>
        {
            if (Instances.WebManager is null)
                return;

            await Instances.WebManager.RestartAsync(
                new()
                {
                    ClosePluginsServer = false,
                    RunPluginsServer = false,
                    CloseDevicesServer = false,
                    RunDevicesServer = false,
                },
                actionBeforeStarting: () => DeviceCases.Clear()
            );
        });

        StopDevicesServerCommand = ReactiveCommand.Create(async () =>
        {
            if (Instances.WebManager is null)
                return;

            await Instances.WebManager.CloseAsync(
                new()
                {
                    ClosePluginsServer = false,
                    CloseDevicesServer = false,
                }
            );

            await Task.Delay(AppConfig.Web.UdpSendFrequency + 200);

            DeviceCases.Clear();
        });
    }

    public override void InitEvents()
    {
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

    internal static ObservableCollection<DeviceCase> DeviceCases => ViewInstances.DeviceCases;

    internal ReactiveCommand<Unit, Task>? RestartDevicesServerCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? StopDevicesServerCommand { get; set; }
}
