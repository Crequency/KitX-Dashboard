using System.Reactive;
using Avalonia;
using KitX.Core.Contract.Configuration;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class SettingsPageViewModel : ViewModelBase
{
    private readonly IConfigService _configService;

    internal SettingsPageViewModel()
    {
        _configService = ConfigService;

        InitCommands();
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

    public override void InitEvents() => throw new System.NotImplementedException();

    internal bool IsPaneOpen
    {
        get => _configService.AppConfig.Pages.Settings.IsNavigationViewPaneOpened;
        set
        {
            _configService.AppConfig.Pages.Settings.IsNavigationViewPaneOpened = value;

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
        get => (FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode)(int)_configService.AppConfig.Pages.Settings.NavigationViewPaneDisplayMode;
        set
        {
            _configService.AppConfig.Pages.Settings.NavigationViewPaneDisplayMode = (KitX.Core.Contract.Configuration.NavigationViewPaneDisplayMode)(int)value;

            this.RaisePropertyChanged(nameof(NavigationViewPaneDisplayMode));

            this.RaisePropertyChanged(nameof(FirstItemMargin));

            _configService.SaveAll();
        }
    }

    internal ReactiveCommand<Unit, Unit>? ResetToAutoCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToLeftCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToTopCommand { get; set; }
}
