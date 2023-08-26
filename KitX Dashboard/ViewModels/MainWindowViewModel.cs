using Avalonia;
using Avalonia.Controls;
using KitX.Dashboard.Data;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive;
using System.Reflection;
using System.Text;

namespace KitX.Dashboard.ViewModels;

internal class MainWindowViewModel : ViewModelBase
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    private void InitCommands()
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

    private void InitEvents()
    {
        Instances.DeviceCards.CollectionChanged += (_, _) =>
        {
            PropertyChanged?.Invoke(this, new(nameof(TrayIconText)));
        };
    }

    internal static string TrayIconText
    {
        get
        {
            var sb = new StringBuilder();

            sb.AppendLine(
                FetchStringFromResource(Application.Current, "Text_MainWindow_Title") ?? "KitX"
            );

            sb.AppendLine($"v{Assembly.GetEntryAssembly()?.GetName().Version}");

            sb.AppendLine();

            sb.AppendLine(
                $"{Instances.DeviceCards.Count} " +
                $"{FetchStringFromResource(Application.Current, "Text_Device_Tip_Detected")}"
            );

            sb.AppendLine(
                $"{Instances.PluginCards.Count} " +
                $"{FetchStringFromResource(Application.Current, "Text_Lib_Tip_Connected")}"
            );

            sb.AppendLine();

            sb.Append("Hello, World!");

            return sb.ToString();
        }
    }

    internal ReactiveCommand<object?, Unit>? TrayIconClickedCommand { get; set; }

    internal ReactiveCommand<object?, Unit>? ExitCommand { get; set; }

    internal ReactiveCommand<object?, Unit>? RefreshGreetingCommand { get; set; }
}
