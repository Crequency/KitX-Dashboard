using System;
using System.Reactive;
using System.Threading.Tasks;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Tasks;
using KitX.Core.Event;
using KitX.Core.Tasks;
using KitX.Dashboard.Services;
using KitX.Dashboard.Utils;
using KitX.Dashboard.Views;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_GeneralViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IAnnouncementService _announcementService;
    private readonly ITasksService _tasksService;

    public Settings_GeneralViewModel(IConfigService configService, IAnnouncementService announcementService, ITasksService tasksService)
    {
        _configService = configService;
        _announcementService = announcementService;
        _tasksService = tasksService;

        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        ShowAnnouncementsInstantlyCommand = ReactiveCommand.Create(() =>
        {
            _tasksService.RunTaskAsync(
                async () => await _announcementService.CheckNewAnnouncementsAsync(),
                nameof(ShowAnnouncementsInstantlyCommand)
            );
        });

        OpenDebugToolCommand = ReactiveCommand.Create(() =>
        {
            UIStateService.ShowWindow(new DebugWindow(), UIStateService.MainWindow);
        });
    }

    public sealed override void InitEvents()
    {
        Events.Subscribe(EventNames.DevelopSettingsChanged, (s, e) => this.RaisePropertyChanged(nameof(DeveloperSettingEnabled)));
    }

    internal string LocalPluginsFileDirectory
    {
        get => _configService.AppConfig.App.LocalPluginsFileFolder;
        set
        {
            _configService.AppConfig.App.LocalPluginsFileFolder = value;
            _configService.SaveAll();
        }
    }

    internal string LocalPluginsDataDirectory
    {
        get => _configService.AppConfig.App.LocalPluginsDataFolder;
        set
        {
            _configService.AppConfig.App.LocalPluginsDataFolder = value;
            _configService.SaveAll();
        }
    }

    internal int ShowAnnouncementsStatus
    {
        get => _configService.AppConfig.App.ShowAnnouncementWhenStart ? 0 : 1;
        set
        {
            _configService.AppConfig.App.ShowAnnouncementWhenStart = value == 0;
            _configService.SaveAll();
        }
    }

    internal bool DeveloperSettingEnabled
    {
        get => _configService.AppConfig.App.DeveloperSetting;
    }

    internal int DeveloperSettingStatus
    {
        get => _configService.AppConfig.App.DeveloperSetting ? 0 : 1;
        set
        {
            _configService.AppConfig.App.DeveloperSetting = value == 0;
            Events.Publish(EventNames.DevelopSettingsChanged);
            _configService.SaveAll();
        }
    }

    internal ReactiveCommand<Unit, Unit>? ShowAnnouncementsInstantlyCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? OpenDebugToolCommand { get; set; }
}
