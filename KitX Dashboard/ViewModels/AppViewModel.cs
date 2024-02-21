using Avalonia;
using Avalonia.Controls;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using ReactiveUI;
using System.Reactive;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace KitX.Dashboard.ViewModels;

internal class AppViewModel : ViewModelBase
{
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
            ViewInstances.PluginsLaunchWindow ??= new();

            var win = ViewInstances.PluginsLaunchWindow;

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

        ViewInstances.PluginConnectors.CollectionChanged += (_, _) => UpdateTrayIconText();
    }

    private void UpdateTrayIconText()
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
            $"{ViewInstances.PluginConnectors.Count} " +
            $"{FetchStringFromResource(Application.Current, "Text_Lib_Tip_Connected")}"
        );

        sb.AppendLine();

        sb.Append("Hello, World!");

        TrayIconText = sb.ToString();
    }

    internal string trayIconText = "";

    internal string TrayIconText
    {
        get => trayIconText;
        set => this.RaiseAndSetIfChanged(
            ref trayIconText,
            value,
            nameof(TrayIconText)
        );
    }

    internal ReactiveCommand<Unit, Unit>? TrayIconClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? ViewLatestAnnouncementsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? ExitCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? PluginLauncherCommand { get; set; }
}
