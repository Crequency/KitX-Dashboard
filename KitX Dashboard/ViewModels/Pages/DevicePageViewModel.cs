using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive;
using System.Threading.Tasks;

namespace KitX.Dashboard.ViewModels.Pages;

internal class DevicePageViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

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
                    RunPluginsServer = false
                },
                actionBeforeStarting: () => DeviceCards.Clear()
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
                    RunPluginsServer = false
                }
            );

            await Task.Delay(Instances.ConfigManager.AppConfig.Web.UdpSendFrequency + 200);

            DeviceCards.Clear();
        });
    }

    public override void InitEvents()
    {
        DeviceCards.CollectionChanged += (_, _) =>
        {
            NoDevice_TipHeight = DeviceCards.Count == 0 ? 300 : 0;
            DevicesCount = DeviceCards.Count.ToString();
        };
    }

    internal string? SearchingText { get; set; }

    internal string devicesCount = DeviceCards.Count.ToString();

    internal string DevicesCount
    {
        get => devicesCount;
        set
        {
            devicesCount = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(DevicesCount))
            );
        }
    }

    internal double noDevice_TipHeight = DeviceCards.Count == 0 ? 300 : 0;

    internal double NoDevice_TipHeight
    {
        get => noDevice_TipHeight;
        set
        {
            noDevice_TipHeight = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(NoDevice_TipHeight))
            );
        }
    }

    internal static ObservableCollection<DeviceCard> DeviceCards => ViewInstances.DeviceCards;

    internal ReactiveCommand<Unit, Task>? RestartDevicesServerCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? StopDevicesServerCommand { get; set; }
}
