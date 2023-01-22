using Avalonia;
using FluentAvalonia.UI.Controls;
using KitX_Dashboard.Commands;
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
        get => Program.Config.Pages.Home.IsNavigationViewPaneOpened;
        set
        {
            Program.Config.Pages.Home.IsNavigationViewPaneOpened = value;
            EventHandlers.Invoke(nameof(EventHandlers.ConfigSettingsChanged));
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
        get => Program.Config.Pages.Home.NavigationViewPaneDisplayMode;
        set
        {
            Program.Config.Pages.Home.NavigationViewPaneDisplayMode = value;
            PropertyChanged?.Invoke(this,
                new(nameof(NavigationViewPaneDisplayMode)));
            PropertyChanged?.Invoke(this,
                new(nameof(FirstItemMargin)));
            EventHandlers.Invoke(nameof(EventHandlers.ConfigSettingsChanged));
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

//          .eee.
//         d"   "$b
//        $ zF $e $$c
//    ..e$     ....$$b
//    .   ^$$$$$$$$$$$$
//                  "$$b
//                   $$$
//                z$$$$%
//             .d$$$$$"
//           .$$$$$$"
//          d$$$$$"
//         $$$$$"
//        .$$$$" .e$$$$$$$$$e.
//        4$$b"3$$$$$$$$$$$$$$$$e
//         $$F  $$$$$$$$$$$$$$$$$$$e
//         *$$.  $$$$$$$$$$$$$$$$$$$$$c
//          $$$.  ^$$$$$$$$$$$$$$$$$$$$$$c
//           *$$c    *$$$$$$$$$$$$$$$$$$$$$$.
//            ^$$b     ^*$$$$$$$$$$$$$$$$$$$$$$c
//              *$$c       "*$$$$$$$$$$$$$$$$$$$$$e.
//                *$$c          ""******"^E""e. "*"
//                  *$$b.               $$$$e. *b. zP.
//                    *$$$e            .*$. *$*4$ "%.  ^
//                    ^$$$$$$c      /"    $c  b. "\  4$@
//                     $$$$$$$$$c="         ^4'$$c $^4$
//                     $$$" *$$$$              *$.*c  b
//                    f*$$    $$$                *  "b*
//                     4$      *$F                 - $
//                   J  P       ^$                   "
//                   "-
//                  4
//                  %-
//                 .
//        .====*""  -  -"""""""
//       F            .         ^
