using System;
using System.Reactive;
using System.Threading.Tasks;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_GeneralViewModel : ViewModelBase
{
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
        EventService.DevelopSettingsChanged += () => this.RaisePropertyChanged(
            nameof(DeveloperSettingEnabled)
        );
    }

    internal static string LocalPluginsFileDirectory
    {
        get => ConfigManager.Instance.AppConfig.App.LocalPluginsFileFolder;
        set
        {
            ConfigManager.Instance.AppConfig.App.LocalPluginsFileFolder = value;
            SaveAppConfigChanges();
        }
    }

    internal static string LocalPluginsDataDirectory
    {
        get => ConfigManager.Instance.AppConfig.App.LocalPluginsDataFolder;
        set
        {
            ConfigManager.Instance.AppConfig.App.LocalPluginsDataFolder = value;
            SaveAppConfigChanges();
        }
    }

    internal static int ShowAnnouncementsStatus
    {
        get => ConfigManager.Instance.AppConfig.App.ShowAnnouncementWhenStart ? 0 : 1;
        set
        {
            ConfigManager.Instance.AppConfig.App.ShowAnnouncementWhenStart = value == 0;
            SaveAppConfigChanges();
        }
    }

    internal static bool DeveloperSettingEnabled
    {
        get => ConfigManager.Instance.AppConfig.App.DeveloperSetting;
    }

    internal static int DeveloperSettingStatus
    {
        get => ConfigManager.Instance.AppConfig.App.DeveloperSetting ? 0 : 1;
        set
        {
            ConfigManager.Instance.AppConfig.App.DeveloperSetting = value == 0;
            EventService.Invoke(nameof(EventService.DevelopSettingsChanged));
            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, Unit>? ShowAnnouncementsInstantlyCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? OpenDebugToolCommand { get; set; }
}
