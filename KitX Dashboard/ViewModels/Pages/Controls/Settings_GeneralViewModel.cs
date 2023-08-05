using Common.BasicHelper.Utils.Extensions;
using Common.ExternalConsole;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_GeneralViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private static ExternalConsolesManager _manager = new();

    private static int _consolesCount = 0;

    internal Settings_GeneralViewModel()
    {
        InitCommands();

        InitEvents();
    }

    private void InitCommands()
    {
        ShowAnnouncementsInstantlyCommand = ReactiveCommand.Create(() =>
        {
            Task.Run(async () =>
            {
                try
                {
                    await AnouncementManager.CheckNewAnnouncements();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"辣鸡公告系统又双叒叕崩了 ! {ex.Message}");
                }
            });
        });

        OpenDebugToolCommand = ReactiveCommand.Create(async () =>
        {
            ++_consolesCount;

            var port = ConfigManager.AppConfig.Web.DebugServicesServerPort;

            if (!_manager.ServerLaunched) _manager = await _manager.LaunchServer(port);

            var name = $"KitX_DebugTool_{_consolesCount}";
            var console = _manager.Register(name);

            new Thread(() =>
            {
                try
                {
                    ProcessStartInfo psi = new()
                    {
                        FileName = Path.GetFullPath($"./Common.ExternalConsole.ExternalConsole" +
                            $"{(OperatingSystem.IsWindows() ? ".exe" : "")}"),
                        Arguments = $"--port {port} --name {name}",
                        CreateNoWindow = false,
                        UseShellExecute = true,
                    };
                    var process = new Process
                    {
                        StartInfo = psi
                    };
                    process.Start();

                    var keepWorking = true;
                    var messages2Send = new Queue<string>()
                        .Push(@"|^disable_debug|")
                    ;

                    async void Reader(StreamReader reader)
                    {
                        try
                        {
                            while (keepWorking)
                            {
                                var message = await reader.ReadLineAsync();

                                switch (message)
                                {
                                    case null:
                                        continue;
                                    case @"|^console_exit|":
                                        keepWorking = false;
                                        break;
                                    case "":
                                        continue;
                                    default:
                                        if (message.Equals(string.Empty)) continue;

                                        var result = DebugService.ExecuteCommand(message);
                                        messages2Send.Enqueue(result ?? "No this command.");
                                        break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            console.Dispose();

                            var location = $"{nameof(Settings_UpdateViewModel)}.{nameof(OpenDebugToolCommand)}";
                            Log.Warning(ex, $"In {location}: {ex.Message}");
                        }
                    }

                    async void Writer(StreamWriter writer)
                    {
                        try
                        {
                            while (keepWorking)
                            {
                                if (messages2Send.Count > 0)
                                {
                                    await writer.WriteLineAsync(
                                        messages2Send.Dequeue()
                                        .Replace("\r\n", "\n")
                                        .Replace("\n", "|^new_line|")
                                    );
                                    await writer.FlushAsync();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await writer.DisposeAsync();
                            console.Dispose();

                            var location = $"{nameof(Settings_GeneralViewModel)}.{nameof(OpenDebugToolCommand)}";
                            Log.Warning(ex, $"In {location}: {ex.Message}");
                        }
                    }

                    console.HandleMessages(Reader, Writer);
                }
                catch (Exception ex)
                {
                    console.Dispose();
                    var location = $"{nameof(Settings_GeneralViewModel)}.{nameof(OpenDebugToolCommand)}";
                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }).Start();
        });
    }

    private void InitEvents()
    {
        EventService.DevelopSettingsChanged += () => PropertyChanged?.Invoke(
            this,
            new(
                nameof(DeveloperSettingEnabled)
            )
        );
    }

    internal static string LocalPluginsFileDirectory
    {
        get => ConfigManager.AppConfig.App.LocalPluginsFileFolder;
        set
        {
            ConfigManager.AppConfig.App.LocalPluginsFileFolder = value;
            SaveAppConfigChanges();
        }
    }

    internal static string LocalPluginsDataDirectory
    {
        get => ConfigManager.AppConfig.App.LocalPluginsDataFolder;
        set
        {
            ConfigManager.AppConfig.App.LocalPluginsDataFolder = value;
            SaveAppConfigChanges();
        }
    }

    internal static int ShowAnnouncementsStatus
    {
        get => ConfigManager.AppConfig.App.ShowAnnouncementWhenStart ? 0 : 1;
        set
        {
            ConfigManager.AppConfig.App.ShowAnnouncementWhenStart = value == 0;
            SaveAppConfigChanges();
        }
    }

    internal static bool DeveloperSettingEnabled
    {
        get => ConfigManager.AppConfig.App.DeveloperSetting;
    }

    internal static int DeveloperSettingStatus
    {
        get => ConfigManager.AppConfig.App.DeveloperSetting ? 0 : 1;
        set
        {
            ConfigManager.AppConfig.App.DeveloperSetting = value == 0;
            EventService.Invoke(nameof(EventService.DevelopSettingsChanged));
            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, Unit>? ShowAnnouncementsInstantlyCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? OpenDebugToolCommand { get; set; }
}
