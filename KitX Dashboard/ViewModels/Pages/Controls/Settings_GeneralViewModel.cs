using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using ReactiveUI;
using Serilog;
using System;
using System.ComponentModel;
using System.Reactive;
using System.Threading.Tasks;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_GeneralViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    internal Settings_GeneralViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        ShowAnnouncementsInstantlyCommand = ReactiveCommand.Create(() =>
        {
            Task.Run(async () =>
            {
                try
                {
                    await AnouncementManager.CheckNewAnnouncements();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"辣鸡公告系统又双叒叕崩了 ! {ex.Message}");
                }
            });
        });

        OpenDebugToolCommand = ReactiveCommand.Create(() =>
        {
            ViewInstances.ShowWindow(new DebugWindow(), ViewInstances.MainWindow);
        });
    }

    public override void InitEvents()
    {
        EventService.DevelopSettingsChanged += () => PropertyChanged?.Invoke(
            this,
            new(
                nameof(DeveloperSettingEnabled)
            )
        );
    }

    internal static string LocalPluginsFileDirectory
    {
        get => Instances.ConfigManager.AppConfig.App.LocalPluginsFileFolder;
        set
        {
            Instances.ConfigManager.AppConfig.App.LocalPluginsFileFolder = value;
            SaveAppConfigChanges();
        }
    }

    internal static string LocalPluginsDataDirectory
    {
        get => Instances.ConfigManager.AppConfig.App.LocalPluginsDataFolder;
        set
        {
            Instances.ConfigManager.AppConfig.App.LocalPluginsDataFolder = value;
            SaveAppConfigChanges();
        }
    }

    internal static int ShowAnnouncementsStatus
    {
        get => Instances.ConfigManager.AppConfig.App.ShowAnnouncementWhenStart ? 0 : 1;
        set
        {
            Instances.ConfigManager.AppConfig.App.ShowAnnouncementWhenStart = value == 0;
            SaveAppConfigChanges();
        }
    }

    internal static bool DeveloperSettingEnabled
    {
        get => Instances.ConfigManager.AppConfig.App.DeveloperSetting;
    }

    internal static int DeveloperSettingStatus
    {
        get => Instances.ConfigManager.AppConfig.App.DeveloperSetting ? 0 : 1;
        set
        {
            Instances.ConfigManager.AppConfig.App.DeveloperSetting = value == 0;
            EventService.Invoke(nameof(EventService.DevelopSettingsChanged));
            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, Unit>? ShowAnnouncementsInstantlyCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? OpenDebugToolCommand { get; set; }
}
