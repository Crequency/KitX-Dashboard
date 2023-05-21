using Avalonia.Controls;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views;
using ReactiveUI;
using System.Reactive;

namespace KitX_Dashboard.ViewModels;

internal class MainWindowViewModel : ViewModelBase
{

    public MainWindowViewModel()
    {
        InitCommands();
    }

    internal void InitCommands()
    {
        TrayIconClickedCommand = ReactiveCommand.Create<object?>(mainWindow =>
        {
            var win = mainWindow as MainWindow;

            if (win?.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;

            win?.Show();

            win?.Activate();

            ConfigManager.AppConfig.Windows.MainWindow.IsHidden = false;

            EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
        });

        ExitCommand = ReactiveCommand.Create<object?>(mainWindow =>
        {
            GlobalInfo.Exiting = true;

            EventService.Invoke(nameof(EventService.OnExiting));

            var win = mainWindow as MainWindow;

            win?.Close();
        });

        RefreshGreetingCommand = ReactiveCommand.Create<object?>(mainWindow =>
        {
            var win = mainWindow as MainWindow;

            win?.UpdateGreetingText();
        });
    }

    internal ReactiveCommand<object?, Unit>? TrayIconClickedCommand { get; set; }

    internal ReactiveCommand<object?, Unit>? ExitCommand { get; set; }

    internal ReactiveCommand<object?, Unit>? RefreshGreetingCommand { get; set; }
}
