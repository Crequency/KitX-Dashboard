using Avalonia;
using FluentAvalonia.UI.Controls;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages;

internal class HomePageViewModel : ViewModelBase, INotifyPropertyChanged
{
    public HomePageViewModel()
    {
        InitCommands();
    }

    internal void InitCommands()
    {
        ResetToAutoCommand = new(ResetToAuto);
        MoveToLeftCommand = new(MoveToLeft);
        MoveToTopCommand = new(MoveToTop);
    }

    internal static bool IsPaneOpen
    {
        get => ConfigManager.AppConfig.Pages.Home.IsNavigationViewPaneOpened;
        set
        {
            ConfigManager.AppConfig.Pages.Home.IsNavigationViewPaneOpened = value;
            EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
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
        get => ConfigManager.AppConfig.Pages.Home.NavigationViewPaneDisplayMode;
        set
        {
            ConfigManager.AppConfig.Pages.Home.NavigationViewPaneDisplayMode = value;
            PropertyChanged?.Invoke(this,
                new(nameof(NavigationViewPaneDisplayMode)));
            PropertyChanged?.Invoke(this,
                new(nameof(FirstItemMargin)));
            EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
        }
    }

    internal DelegateCommand? ResetToAutoCommand { get; set; }

    internal DelegateCommand? MoveToLeftCommand { get; set; }

    internal DelegateCommand? MoveToTopCommand { get; set; }

    internal void ResetToAuto(object? _)
        => NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Auto;

    internal void MoveToLeft(object? _)
        => NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Left;

    internal void MoveToTop(object? _)
        => NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Top;

    public new event PropertyChangedEventHandler? PropertyChanged;
}
