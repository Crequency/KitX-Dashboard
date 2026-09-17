using System.Reactive;
using Avalonia;
using KitX.Core.Contract.Configuration;
using ReactiveUI;
using ContractPaneDisplayMode = KitX.Core.Contract.Configuration.NavigationViewPaneDisplayMode;
using UiPaneDisplayMode = FluentAvalonia.UI.Controls.NavigationViewPaneDisplayMode;

namespace KitX.Dashboard.ViewModels.Pages;

/// <summary>
/// Shared navigation-pane state (commands + layout properties) for pages that host
/// a NavigationView whose pane mode is user-configurable (Home / Settings). The
/// concrete config section is provided by derived classes (D6 convergence).
/// </summary>
internal abstract class NavigationPaneViewModelBase : ViewModelBase
{
    protected readonly IConfigService _configService;

    protected NavigationPaneViewModelBase(IConfigService configService)
    {
        _configService = configService;

        InitCommands();

        InitEvents();
    }

    /// <summary>
    /// Pane display mode persisted in this page's config section.
    /// </summary>
    protected abstract ContractPaneDisplayMode ConfigPaneDisplayMode { get; set; }

    /// <summary>
    /// Pane opened state persisted in this page's config section.
    /// </summary>
    protected abstract bool ConfigPaneOpened { get; set; }

    public override void InitCommands()
    {
        ResetToAutoCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = UiPaneDisplayMode.Auto;
        });

        MoveToLeftCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = UiPaneDisplayMode.Left;
        });

        MoveToTopCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = UiPaneDisplayMode.Top;
        });
    }

    public override void InitEvents() { }

    internal bool IsPaneOpen
    {
        get => ConfigPaneOpened;
        set
        {
            ConfigPaneOpened = value;

            _configService.SaveAll();
        }
    }

    internal Thickness FirstItemMargin =>
        NavigationViewPaneDisplayMode switch
        {
            UiPaneDisplayMode.Auto => new(0, 5, 0, 0),
            UiPaneDisplayMode.Left => new(0, 5, 0, 0),
            UiPaneDisplayMode.LeftCompact => new(0, 5, 0, 0),
            UiPaneDisplayMode.LeftMinimal => new(0, 5, 0, 0),
            UiPaneDisplayMode.Top => new(0, 0, 0, 0),
            _ => new(0, 0, 0, 0),
        };

    internal UiPaneDisplayMode NavigationViewPaneDisplayMode
    {
        get => (UiPaneDisplayMode)(int)ConfigPaneDisplayMode;
        set
        {
            ConfigPaneDisplayMode = (ContractPaneDisplayMode)(int)value;

            this.RaisePropertyChanged(nameof(NavigationViewPaneDisplayMode));

            this.RaisePropertyChanged(nameof(FirstItemMargin));

            _configService.SaveAll();
        }
    }

    internal ReactiveCommand<Unit, Unit>? ResetToAutoCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToLeftCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToTopCommand { get; set; }
}
