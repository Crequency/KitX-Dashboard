using Avalonia;
using Avalonia.Controls;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

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

    public override void InitCommands()
    {
        TrayIconClickedCommand = ReactiveCommand.Create(() =>
        {
            var win = ViewInstances.MainWindow;

            if (win?.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;

            win?.Show();

            win?.Activate();

            Instances.ConfigManager.AppConfig.Windows.MainWindow.IsHidden = false;

            SaveAppConfigChanges();
        });

        ViewLatestAnnouncementsCommand = ReactiveCommand.Create(async () =>
        {
            await AnouncementManager.CheckNewAnnouncements();
        });

        PluginLauncherCommand = ReactiveCommand.Create(() =>
        {
            var win = ViewInstances.PluginsLaunchWindow;

            if (win is null)
            {
                win = new PluginsLaunchWindow();

                ViewInstances.PluginsLaunchWindow = win;
            }

            if (win.IsVisible)
            {
                win.Hide();

                return;
            }

            win.Show();

            win.Activate();
        });

        ExitCommand = ReactiveCommand.Create(() =>
        {
            ConstantTable.Exiting = true;

            EventService.Invoke(nameof(EventService.OnExiting));

            var win = ViewInstances.MainWindow;

            win?.Close();
        });
    }

    public override void InitEvents()
    {
        ViewInstances.DeviceCards.CollectionChanged += (_, _) => UpdateTrayIconText();

        ViewInstances.PluginCards.CollectionChanged += (_, _) => UpdateTrayIconText();
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
                $"{ViewInstances.DeviceCards.Count} " +
                $"{FetchStringFromResource(Application.Current, "Text_Device_Tip_Detected")}"
            );

            sb.AppendLine(
                $"{ViewInstances.PluginCards.Count} " +
                $"{FetchStringFromResource(Application.Current, "Text_Lib_Tip_Connected")}"
            );

            sb.AppendLine();

            sb.Append("Hello, World!");

            return sb.ToString();
        }
    }

    internal ReactiveCommand<Unit, Unit>? TrayIconClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? ViewLatestAnnouncementsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? ExitCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? PluginLauncherCommand { get; set; }
}
