using System;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Event;
using KitX.Dashboard;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using ReactiveUI;
using WindowState = Avalonia.Controls.WindowState;

namespace KitX.Dashboard.ViewModels;

internal class AppViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IAnnouncementService _announcementService;

    public AppViewModel()
    {
        // Get services from DI container
        _configService = ConfigService;
        _announcementService = AnnouncementService;

        InitCommands();

        InitEvents();

        UpdateTrayIconText();
    }

    public sealed override void InitCommands()
    {
        TrayIconClickedCommand = ReactiveCommand.Create(() =>
        {
            var win = UIStateService.MainWindow;

            if (win?.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;

            win?.Show();

            win?.Activate();

            _configService.AppConfig.Windows.MainWindow.IsHidden = false;

            _configService.SaveAll();
        });

        ViewLatestAnnouncementsCommand = ReactiveCommand.Create(async () =>
        {
            await _announcementService.CheckNewAnnouncementsAsync();
        });

        OpenDebugToolCommand = ReactiveCommand.Create(() =>
        {
            UIStateService.ShowWindow(new DebugWindow());
        });

        PluginLauncherCommand = ReactiveCommand.Create(() =>
        {
            UIStateService.PluginsLaunchWindow ??= new();

            var win = UIStateService.PluginsLaunchWindow;

            if (win.IsVisible)
            {
                win.Hide();

                return;
            }

            win.Show();

            win.Activate();
        });

        RestartCommand = ReactiveCommand.Create(() =>
        {
            ConstantTable.Restarting = true;

            Exit();
        });

        ExitCommand = ReactiveCommand.Create(Exit);
    }

    public sealed override void InitEvents()
    {
        UIStateService.DeviceCases.CollectionChanged += (_, _) => UpdateTrayIconText();

        UIStateService.PluginInfos.CollectionChanged += (_, _) => UpdateTrayIconText();

        var eventService = App.GetService<IEventService>();
        eventService.Subscribe<PortChangedEventArgs>(EventNames.DevicesServerPortChanged, (s, e) => UpdateTrayIconText());

        eventService.Subscribe<PortChangedEventArgs>(EventNames.PluginsServerPortChanged, (s, e) => UpdateTrayIconText());

        // Subscribe to announcement events to show announcement window
        _announcementService.NewAnnouncementsAvailable += (_, e) =>
        {
            // Convert IAnnouncement list to Dictionary<string, string> format (Legacy compatible)
            var src = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var announcement in e.Announcements)
            {
                src[announcement.PublishDate.ToString("yyyy-MM-dd HH:mm")] = $"# {announcement.Title}\n\n{announcement.Content}";
            }

            if (src.Count > 0)
            {
                var window = new AnnouncementsWindow().UpdateSource(src);
                UIStateService.ShowWindow(window);
            }
        };

        // Subscribe to plugin server events to update UIStateService.PluginInfos
        if (Instances.PluginsServer is not null)
        {
            Instances.PluginsServer.PluginRegistered += (_, args) =>
            {
                if (args.PluginInfo is not null && !UIStateService.PluginInfos.Any(x => x.Equals(args.PluginInfo)))
                {
                    UIStateService.PluginInfos.Add(args.PluginInfo);
                }
            };

            Instances.PluginsServer.PluginUnregistered += (_, args) =>
            {
                if (args.PluginInfo is not null)
                {
                    var existing = UIStateService.PluginInfos.FirstOrDefault(x => x.Equals(args.PluginInfo));
                    if (existing is not null)
                    {
                        UIStateService.PluginInfos.Remove(existing);
                    }
                }
            };
        }
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
            .Append(UIStateService.DeviceCases.Count + " ")
            .AppendLine(Translate("Text_Device_Tip_Detected"))
            .Append(UIStateService.PluginInfos.Count + " ")
            .AppendLine(Translate("Text_Lib_Tip_Connected"))
            .AppendLine()
            .Append("Hello, World!");

        TrayIconText = sb.ToString();
    }

    public static void Exit()
    {
        UIStateService.DeviceCases.Clear();

        UIStateService.PluginInfos.Clear();

        ConstantTable.Exiting = true;

        var eventService = App.GetService<IEventService>();
        eventService.Publish(EventNames.OnExiting, EventArgs.Empty);

        var win = UIStateService.MainWindow;

        win?.Close();
    }

    internal string trayIconText = "";

    internal string TrayIconText
    {
        get => trayIconText;
        set => this.RaiseAndSetIfChanged(ref trayIconText, value, nameof(TrayIconText));
    }

    internal ReactiveCommand<Unit, Unit>? TrayIconClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? ViewLatestAnnouncementsCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? OpenDebugToolCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RestartCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? ExitCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? PluginLauncherCommand { get; set; }
}
