using System.Reactive;
using Avalonia;
using KitX.Core.Contract.Configuration;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class HomePageViewModel : ViewModelBase
{
    private readonly IConfigService _configService;

    public HomePageViewModel()
    {
        _configService = ConfigService;

        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        ResetToAutoCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode.Auto;
        });

        MoveToLeftCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode.Left;
        });

        MoveToTopCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode.Top;
        });
    }

    public override void InitEvents() { }

    internal bool IsPaneOpen
    {
        get => _configService.AppConfig.Pages.Home.IsNavigationViewPaneOpened;
        set
        {
            _configService.AppConfig.Pages.Home.IsNavigationViewPaneOpened = value;

            _configService.SaveAll();
        }
    }

    internal Thickness FirstItemMargin =>
        NavigationViewPaneDisplayMode switch
        {
            FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode.Auto => new(0, 5, 0, 0),
            FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode.Left => new(0, 5, 0, 0),
            FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode.LeftCompact => new(0, 5, 0, 0),
            FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode.LeftMinimal => new(0, 5, 0, 0),
            FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode.Top => new(0, 0, 0, 0),
            _ => new(0, 0, 0, 0),
        };

    internal FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode
    {
        get => (FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode)(int)ConfigService.AppConfig.Pages.Home.NavigationViewPaneDisplayMode;
        set
        {
            ConfigService.AppConfig.Pages.Home.NavigationViewPaneDisplayMode = (KitX.Core.Contract.Configuration.NavigationViewPaneDisplayMode)(int)value;

            this.RaisePropertyChanged(nameof(NavigationViewPaneDisplayMode));

            this.RaisePropertyChanged(nameof(FirstItemMargin));

            ConfigService.SaveAll();
        }
    }

    internal ReactiveCommand<Unit, Unit>? ResetToAutoCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToLeftCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToTopCommand { get; set; }
}
