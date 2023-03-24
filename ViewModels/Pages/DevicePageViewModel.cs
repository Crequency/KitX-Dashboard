using KitX_Dashboard.Commands;
using KitX_Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;

namespace KitX_Dashboard.ViewModels.Pages;

internal class DevicePageViewModel : ViewModelBase, INotifyPropertyChanged
{
    public DevicePageViewModel()
    {
        DeviceCards.CollectionChanged += (_, _) =>
        {
            NoDevice_TipHeight = DeviceCards.Count == 0 ? 300 : 0;
            DevicesCount = DeviceCards.Count.ToString();
        };

        RestartDevicesServerCommand = new(RestartDevicvesServer);
        StopDevicesServerCommand = new(StopDevicvesServer);
    }

    internal string? SearchingText { get; set; }

    internal string devicesCount = DeviceCards.Count.ToString();

    internal string DevicesCount
    {
        get => devicesCount;
        set
        {
            devicesCount = value;
            PropertyChanged?.Invoke(this,
                new(nameof(DevicesCount)));
        }
    }

    internal double noDevice_TipHeight = DeviceCards.Count == 0 ? 300 : 0;

    internal double NoDevice_TipHeight
    {
        get => noDevice_TipHeight;
        set
        {
            noDevice_TipHeight = value;
            PropertyChanged?.Invoke(this,
                new(nameof(NoDevice_TipHeight)));
        }
    }

    internal DelegateCommand? RestartDevicesServerCommand { get; set; }

    internal DelegateCommand? StopDevicesServerCommand { get; set; }

    internal static void RestartDevicvesServer(object _)
    {
        Program.WebManager?.Restart(
            restartPluginsServer: false,
            actionBeforeStarting: () => DeviceCards.Clear()
        );
    }

    internal static void StopDevicvesServer(object _)
    {
        Program.WebManager?.Stop(stopPluginsServer: false);

        Task.Run(async () =>
        {
            await Task.Delay(Program.Config.Web.UDPSendFrequency + 200);
            DeviceCards.Clear();
        });
    }

    internal static ObservableCollection<DeviceCard> DeviceCards => Program.DeviceCards;

    public new event PropertyChangedEventHandler? PropertyChanged;
}

//        ___________________________________
//       |.-.--.--.--.--.--.--.--.--.--.--.-.|
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||_|__|__|__|__|__|__|__|__|__|__|_||
//       [_&gt;_______________________________&lt;_]
//       ||"|""|""|""|""|""|""|""|""|""|""|"||
//       || |  |  |  |  |  |  |  |  |  |  | ||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//  aac  |'-'--'--'--'--'--'--'--'--'--'--'-'|
//       `"""""""""""""""""""""""""""""""""""`
