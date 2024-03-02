using System.Reactive;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using ReactiveUI;

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

            ConfigManager.Instance.AppConfig.Windows.MainWindow.IsHidden = false;

            SaveAppConfigChanges();
        });

        ViewLatestAnnouncementsCommand = ReactiveCommand.Create(async () =>
        {
            await AnouncementManager.CheckNewAnnouncements();
        });

        OpenDebugToolCommand = ReactiveCommand.Create(() =>
        {
            ViewInstances.ShowWindow(new DebugWindow());
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
            ViewInstances.DeviceCases.Clear();

            ViewInstances.PluginInfos.Clear();

            ConstantTable.Exiting = true;

            EventService.Invoke(nameof(EventService.OnExiting));

            var win = ViewInstances.MainWindow;

            win?.Close();
        });
    }

    public override void InitEvents()
    {
        ViewInstances.DeviceCases.CollectionChanged += (_, _) => UpdateTrayIconText();

        ViewInstances.PluginInfos.CollectionChanged += (_, _) => UpdateTrayIconText();

        EventService.DevicesServerPortChanged += _ => UpdateTrayIconText();

        EventService.PluginsServerPortChanged += _ => UpdateTrayIconText();
    }

    private void UpdateTrayIconText()
    {
        var sb = new StringBuilder()
            .AppendLine(Translate("Text_MainWindow_Title") ?? "KitX")
            .AppendLine($"v{Assembly.GetEntryAssembly()?.GetName().Version}")
            .AppendLine()
            .Append(Translate("Text_Settings_Performence_Web_DevicesServerPort"))
            .AppendLine(": " + ConstantTable.DevicesServerPort)
            .Append(Translate("Text_Settings_Performence_Web_PluginsServerPort"))
            .AppendLine(": " + ConstantTable.PluginsServerPort)
            .AppendLine()
            .Append(ViewInstances.DeviceCases.Count + " ")
            .AppendLine(Translate("Text_Device_Tip_Detected"))
            .Append(ViewInstances.PluginInfos.Count + " ")
            .AppendLine(Translate("Text_Lib_Tip_Connected"))
            .AppendLine()
            .Append("Hello, World!")
            ;

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

    internal ReactiveCommand<Unit, Unit>? OpenDebugToolCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? ExitCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? PluginLauncherCommand { get; set; }
}
