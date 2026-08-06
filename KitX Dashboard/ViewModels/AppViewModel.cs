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
using KitX.Core.Contract.Plugin.Events;
using KitX.Dashboard;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using ReactiveUI;
using Serilog;
using WindowState = Avalonia.Controls.WindowState;

namespace KitX.Dashboard.ViewModels;

internal class AppViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IAnnouncementService _announcementService;
    private readonly IEventService _eventService;

    public AppViewModel(
        IConfigService configService,
        IAnnouncementService announcementService,
        IEventService eventService)
    {
        _configService = configService;
        _announcementService = announcementService;
        _eventService = eventService;

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
            // D13.4: developer gate — the debug tool only opens when Developer Setting is on.
            if (!_configService.AppConfig.App.DeveloperSetting)
                return;

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

        // Subscribe to port changes via EventService to update tray icon
        _eventService.Subscribe<PortChangedEventArgs>(EventNames.DevicesServerPortChanged, (s, e) => UpdateTrayIconText());

        _eventService.Subscribe<PortChangedEventArgs>(EventNames.PluginsServerPortChanged, (s, e) => UpdateTrayIconText());

        // Subscribe to plugin events via EventService to update UIStateService.PluginInfos
        _eventService.Subscribe<PluginRegisteredEventArgs>(EventNames.PluginRegistered, (s, e) =>
        {
            Log.Information($"[AppViewModel] Received PluginRegistered event for: {e.PluginInfo?.Name}");
            if (e.PluginInfo is not null && !UIStateService.PluginInfos.Any(x => x.Name == e.PluginInfo.Name))
            {
                // D-REG: plugin events arrive on the server thread — mutate the
                // UI-bound collection on the UI thread.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (UIStateService.PluginInfos.Any(x => x.Name == e.PluginInfo!.Name))
                        return;
                    UIStateService.PluginInfos.Add(e.PluginInfo);
                    Log.Information($"[AppViewModel] Added plugin: {e.PluginInfo.Name}, count: {UIStateService.PluginInfos.Count}");
                });
            }
        });

        _eventService.Subscribe<PluginUnregisteredEventArgs>(EventNames.PluginUnregistered, (s, e) =>
        {
            Log.Information($"[AppViewModel] Received PluginUnregistered event for: {e.PluginInfo?.Name}");
            if (e.PluginInfo is not null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var existing = UIStateService.PluginInfos.FirstOrDefault(x => x.Name == e.PluginInfo!.Name);
                    if (existing is not null)
                    {
                        UIStateService.PluginInfos.Remove(existing);
                        Log.Information($"[AppViewModel] Removed plugin: {e.PluginInfo.Name}, count: {UIStateService.PluginInfos.Count}");
                    }
                    else
                    {
                        Log.Warning($"[AppViewModel] Plugin not found in list: {e.PluginInfo.Name}");
                    }
                });
            }
        });

        // Subscribe to plugin disconnected events to update UIStateService.PluginInfos
        _eventService.Subscribe<PluginConnectionEventArgs>(EventNames.PluginDisconnected, (s, e) =>
        {
            Log.Information($"[AppViewModel] Received PluginDisconnected event for: {e.PluginInfo?.Name}, connection: {e.ConnectionId}");
            if (e.PluginInfo is not null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var existing = UIStateService.PluginInfos.FirstOrDefault(x => x.Name == e.PluginInfo!.Name);
                    if (existing is not null)
                    {
                        UIStateService.PluginInfos.Remove(existing);
                        Log.Information($"[AppViewModel] Removed disconnected plugin: {e.PluginInfo.Name}, count: {UIStateService.PluginInfos.Count}");
                    }
                    else
                    {
                        Log.Warning($"[AppViewModel] Disconnected plugin not found in list: {e.PluginInfo.Name}");
                    }
                });
            }
        });

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
    }

    private void UpdateTrayIconText()
    {
        var sb = new StringBuilder()
            .AppendLine(Translate("Text_MainWindow_Title") ?? "KitX")
            .AppendLine($"v{Assembly.GetEntryAssembly()?.GetName().Version}")
            .AppendLine()
            .Append(Translate("Text_Settings_Performance_Web_DevicesServerPort"))
            .AppendLine(": " + ConstantTable.DevicesServerPort)
            .Append(Translate("Text_Settings_Performance_Web_PluginsServerPort"))
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

    public void Exit()
    {
        UIStateService.DeviceCases.Clear();

        UIStateService.PluginInfos.Clear();

        ConstantTable.Exiting = true;

        _eventService.Publish(EventNames.OnExiting, EventArgs.Empty);

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
