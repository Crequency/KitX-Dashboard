using Avalonia;
using FluentAvalonia.UI.Controls;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Services;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages;

internal class SettingsPageViewModel : ViewModelBase, INotifyPropertyChanged
{
    internal SettingsPageViewModel()
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
        get => Program.Config.Pages.Settings.IsNavigationViewPaneOpened;
        set
        {
            Program.Config.Pages.Settings.IsNavigationViewPaneOpened = value;
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
        get => Program.Config.Pages.Settings.NavigationViewPaneDisplayMode;
        set
        {
            Program.Config.Pages.Settings.NavigationViewPaneDisplayMode = value;
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

    internal void ResetToAuto(object _)
        => NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Auto;

    internal void MoveToLeft(object _)
        => NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Left;

    internal void MoveToTop(object _)
        => NavigationViewPaneDisplayMode = NavigationViewPaneDisplayMode.Top;

    public new event PropertyChangedEventHandler? PropertyChanged;
}

//                       z$6*#""""*c     :@$$****$$$$L
//                    .@$F          "N..$F         '*$$
//                   /$F             '$P             '$$r
//                  d$"                                #$      '%C"""$
//                 4$F                                  $k    ud@$ JP
//                 M$                                   J$*Cz*#" Md"
//                 MR                              'dCum#$       "
//                 MR                               )    $
//                 4$                                   4$
//                  $L                                  MF
//                  '$                                 4$
//                   ?B .z@r                           $
//                 .+(2d"" ?                          $~
//      +$c  .z4Cn*"   "$.                           $
//  '#*M3$Eb*""         '$c                         $
//     /$$RR              #b                      .R
//     6*"                 ^$L                   JF
//                           "$                 $
//                             "b             u"
//                               "N.        xF
//                                 '*c    zF
//                                    "N@"
