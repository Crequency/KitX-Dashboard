﻿using Avalonia;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive;

namespace KitX.Dashboard.ViewModels.Pages;

internal class HomePageViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public HomePageViewModel()
    {
        InitCommands();
    }

    internal void InitCommands()
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

            PropertyChanged?.Invoke(
                this,
                new(nameof(NavigationViewPaneDisplayMode))
            );

            PropertyChanged?.Invoke(
                this,
                new(nameof(FirstItemMargin))
            );

            EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
        }
    }

    internal ReactiveCommand<Unit, Unit>? ResetToAutoCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToLeftCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? MoveToTopCommand { get; set; }
}