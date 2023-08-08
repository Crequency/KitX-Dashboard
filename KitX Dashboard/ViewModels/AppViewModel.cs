using Avalonia;
using Avalonia.Controls;
using KitX.Dashboard.Data;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive;
using System.Reflection;
using System.Text;

namespace KitX.Dashboard.ViewModels;

internal class AppViewModel : ViewModelBase
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public AppViewModel()
    {
        InitCommands();

        InitEvents();

        UpdateTrayIconText();
    }

    private void InitCommands()
    {
        TrayIconClickedCommand = ReactiveCommand.Create(() =>
        {
            var win = Instances.MainWindow;

            if (win?.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;

            win?.Show();

            win?.Activate();

            ConfigManager.AppConfig.Windows.MainWindow.IsHidden = false;

            EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
        });

        ExitCommand = ReactiveCommand.Create(() =>
        {
            GlobalInfo.Exiting = true;

            EventService.Invoke(nameof(EventService.OnExiting));

            var win = Instances.MainWindow;

            win?.Close();
        });
    }

    private static void InitEvents()
    {
        Instances.DeviceCards.CollectionChanged +=
            (_, _) => UpdateTrayIconText();

        Instances.PluginCards.CollectionChanged +=
            (_, _) => UpdateTrayIconText();
    }

    private static void UpdateTrayIconText()
    {
        if (Application.Current is not null)
            Application.Current.Resources[nameof(TrayIconText)] = TrayIconText;
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

    internal ReactiveCommand<Unit, Unit>? TrayIconClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? ExitCommand { get; set; }
}
