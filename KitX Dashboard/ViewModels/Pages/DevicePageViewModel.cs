﻿using KitX.Dashboard.Managers;
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

    private void InitCommands()
    {

        RestartDevicesServerCommand = ReactiveCommand.Create(() =>
        {
            Instances.WebManager?.Restart(
                restartAll: false,
                restartDevicesServices: true,
                restartDevicesDiscoveryServer: true,
                restartPluginsServices: false,
                actionBeforeStarting: () => DeviceCards.Clear()
            );
        });

        StopDevicesServerCommand = ReactiveCommand.Create(() =>
        {
            Task.Run(async () =>
            {
                Instances.WebManager?.Stop(
                    stopAll: false,
                    stopDevicesServices: true,
                    stopDevicesDiscoveryServer: true,
                    stopPluginsServices: false
                );

                await Task.Delay(ConfigManager.AppConfig.Web.UDPSendFrequency + 200);

                DeviceCards.Clear();
            });
        });
    }

    private void InitEvents()
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

    internal static ObservableCollection<DeviceCard> DeviceCards => Instances.DeviceCards;

    internal ReactiveCommand<Unit, Unit>? RestartDevicesServerCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? StopDevicesServerCommand { get; set; }
}