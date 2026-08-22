using KitX.Core.Contract.Configuration;
using ContractPaneDisplayMode = KitX.Core.Contract.Configuration.NavigationViewPaneDisplayMode;

namespace KitX.Dashboard.ViewModels.Pages;

internal class HomePageViewModel : NavigationPaneViewModelBase
{
    public HomePageViewModel(IConfigService configService) : base(configService) { }

    protected override ContractPaneDisplayMode ConfigPaneDisplayMode
    {
        get => _configService.AppConfig.Pages.Home.NavigationViewPaneDisplayMode;
        set => _configService.AppConfig.Pages.Home.NavigationViewPaneDisplayMode = value;
    }

    protected override bool ConfigPaneOpened
    {
        get => _configService.AppConfig.Pages.Home.IsNavigationViewPaneOpened;
        set => _configService.AppConfig.Pages.Home.IsNavigationViewPaneOpened = value;
    }
}
