using Avalonia;
using Avalonia.Controls;
using DesktopNotifications;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views;
using Serilog;
using System;
using System.IO;
using System.Threading;

namespace KitX_Dashboard.ViewModels
{
    internal class MainWindowViewModel : ViewModelBase
    {
        private static bool _firstTime2RefreshGreeting = true;

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
            MainWindow? win = mainWindow as MainWindow;
            if (win?.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;
            win?.Show();
            win?.Activate();
        }

        internal void Exit(object mainWindow)
        {
            MainWindow? win = mainWindow as MainWindow;
            GlobalInfo.Exiting = true;
            EventHandlers.Invoke(nameof(EventHandlers.OnExiting));
            win?.Close();

            new Thread(() =>
            {
                Thread.Sleep(GlobalInfo.LastBreakAfterExit);
                Environment.Exit(0);
            }).Start();
        }

        internal void RefreshGreeting(object mainWindow)
        {
            MainWindow? win = mainWindow as MainWindow;
            win?.UpdateGreetingText();
            if (_firstTime2RefreshGreeting)
            {
                _firstTime2RefreshGreeting = false;
                try
                {
                    var notificationManager =
                        AvaloniaLocator.Current.GetService<INotificationManager>()
                        ?? throw new InvalidDataException("Missing notification manager.");
                    notificationManager.ShowNotification(new()
                    {
                        Title = GlobalInfo.AppName,
                        Body = "(ノω<。)ノ))☆.。",
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, ex.Message);
                }
            }
        }
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
