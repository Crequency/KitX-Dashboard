using Avalonia.Controls;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views;

namespace KitX_Dashboard.ViewModels;

internal class MainWindowViewModel : ViewModelBase
{

    public MainWindowViewModel()
    {
        InitCommands();
    }

    internal void InitCommands()
    {
        TrayIconClickedCommand = new(TrayIconClicked);
        ExitCommand = new(Exit);
        RefreshGreetingCommand = new(RefreshGreeting);
    }

    internal DelegateCommand? TrayIconClickedCommand { get; set; }

    internal DelegateCommand? ExitCommand { get; set; }

    internal DelegateCommand? RefreshGreetingCommand { get; set; }

    internal void TrayIconClicked(object mainWindow)
    {
        var win = mainWindow as MainWindow;

        if (win?.WindowState == WindowState.Minimized)
            win.WindowState = WindowState.Normal;
        win?.Show();
        win?.Activate();

        Program.Config.Windows.MainWindow.IsHidden = false;
        EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
    }

    internal void Exit(object mainWindow)
    {
        GlobalInfo.Exiting = true;
        EventService.Invoke(nameof(EventService.OnExiting));

        var win = mainWindow as MainWindow;
        win?.Close();
    }

    internal void RefreshGreeting(object mainWindow)
    {
        var win = mainWindow as MainWindow;
        win?.UpdateGreetingText();
    }
}

//         g"y----____
//         HmH--__    ~~~~----____
//        ,%%%.   ~~--__          ~~~~----____
//        JMMML         ~~--__                ~~~~----____
//        |%%%|               ~~--__                      ~~~~----____
//       ,MMMMM.                    ~~--__                            ~~~~-
//       |%%%%%|                          ~~--__
//       AMMMMMA                                ~~--__
//    ___MMM^MMM__                                    ~~--__
//                `\AwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAwAw^v^v^v^v^v^v^v^
