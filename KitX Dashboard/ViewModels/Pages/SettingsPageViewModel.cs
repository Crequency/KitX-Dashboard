using KitX.Core.Contract.Configuration;
using ContractPaneDisplayMode = KitX.Core.Contract.Configuration.NavigationViewPaneDisplayMode;

namespace KitX.Dashboard.ViewModels.Pages;

internal class SettingsPageViewModel : NavigationPaneViewModelBase
{
    public SettingsPageViewModel(IConfigService configService) : base(configService) { }

    protected override ContractPaneDisplayMode ConfigPaneDisplayMode
    {
        get => _configService.AppConfig.Pages.Settings.NavigationViewPaneDisplayMode;
        set => _configService.AppConfig.Pages.Settings.NavigationViewPaneDisplayMode = value;
    }

    protected override bool ConfigPaneOpened
    {
        get => _configService.AppConfig.Pages.Settings.IsNavigationViewPaneOpened;
        set => _configService.AppConfig.Pages.Settings.IsNavigationViewPaneOpened = value;
    }
}
