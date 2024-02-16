using Avalonia;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Services;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive;

namespace KitX.Dashboard.ViewModels.Pages;

internal class SettingsPageViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    internal SettingsPageViewModel()
    {
        InitCommands();
    }

    public override void InitCommands()
    {
        ResetToAutoCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
        });

        MoveToLeftCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        });

        MoveToTopCommand = ReactiveCommand.Create(() =>
        {
            NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Top;
        });
    }

    public override void InitEvents() => throw new System.NotImplementedException();

    internal static bool IsPaneOpen
    {
        get => Instances.ConfigManager.AppConfig.Pages.Settings.IsNavigationViewPaneOpened;
        set
        {
            Instances.ConfigManager.AppConfig.Pages.Settings.IsNavigationViewPaneOpened = value;

            SaveAppConfigChanges();
        }
    }

    internal Thickness FirstItemMargin => NavigationViewPaneDisplayMode switch
    {
        NavigationViewPaneDisplayMode.Auto => new(0, 5, 0, 0),
        NavigationViewPaneDisplayMode.Left => new(0, 5, 0, 0),
        NavigationViewPaneDisplayMode.LeftCompact => new(0, 5, 0, 0),
        NavigationViewPaneDisplayMode.LeftMinimal => new(0, 5, 0, 0),
        NavigationViewPaneDisplayMode.Top => new(0, 0, 0, 0),
        _ => new(0, 0, 0, 0)
    };

    internal NavigationViewPaneDisplayMode NavigationViewPaneDisplayMode
    {
        get => Instances.ConfigManager.AppConfig.Pages.Settings.NavigationViewPaneDisplayMode;
        set
        {
            Instances.ConfigManager.AppConfig.Pages.Settings.NavigationViewPaneDisplayMode = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(NavigationViewPaneDisplayMode))
            );

            PropertyChanged?.Invoke(
                this,
                new(nameof(FirstItemMargin))
            );

            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, Unit>? ResetToAutoCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToLeftCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToTopCommand { get; set; }
}
