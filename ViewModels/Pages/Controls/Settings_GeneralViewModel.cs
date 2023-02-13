using Common.BasicHelper.Util.Extension;
using Common.ExternalConsole;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class Settings_GeneralViewModel : ViewModelBase, INotifyPropertyChanged
{

    private static ExternalConsolesManager _manager = new();
    private static int _consolesCount = 0;

    internal Settings_GeneralViewModel()
    {
        InitCommands();

        InitEvents();
    }

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitCommands()
    {
        ShowAnnouncementsNowCommand = new(ShowAnnouncementsNow);
        OpenDebugToolCommand = new(OpenDebugTool);
    }

    /// <summary>
    /// 初始化事件
    /// </summary>
    private void InitEvents()
    {
        EventService.DevelopSettingsChanged +=
            () => PropertyChanged?.Invoke(this,
            new(nameof(DeveloperSettingEnabled)));
    }

    /// <summary>
    /// 保存变更
    /// </summary>
    private static void SaveChanges()
    {
        EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
    }

    /// <summary>
    /// 本地插件程序目录
    /// </summary>
    internal static string LocalPluginsFileDirectory
    {
        get => Program.Config.App.LocalPluginsFileFolder;
        set
        {
            Program.Config.App.LocalPluginsFileFolder = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 本地插件数据目录
    /// </summary>
    internal static string LocalPluginsDataDirectory
    {
        get => Program.Config.App.LocalPluginsDataFolder;
        set
        {
            Program.Config.App.LocalPluginsDataFolder = value;
            SaveChanges();
        }
    }

    /// <summary>
    /// 是否在启动时显示公告
    /// </summary>
    internal static int ShowAnnouncementsStatus
    {
        get => Program.Config.App.ShowAnnouncementWhenStart ? 0 : 1;
        set
        {
            Program.Config.App.ShowAnnouncementWhenStart = value == 0;
            SaveChanges();
        }
    }

    /// <summary>
    /// 是否启用了开发者设置
    /// </summary>
    internal static bool DeveloperSettingEnabled
    {
        get => Program.Config.App.DeveloperSetting;
    }

    /// <summary>
    /// 开发者设置项
    /// </summary>
    internal static int DeveloperSettingStatus
    {
        get => Program.Config.App.DeveloperSetting ? 0 : 1;
        set
        {
            Program.Config.App.DeveloperSetting = value == 0;
            EventService.Invoke(nameof(EventService.DevelopSettingsChanged));
            SaveChanges();
        }
    }

    /// <summary>
    /// 立即显示公告命令
    /// </summary>
    internal DelegateCommand? ShowAnnouncementsNowCommand { get; set; }

    /// <summary>
    /// 打开调试工具命令
    /// </summary>
    internal DelegateCommand? OpenDebugToolCommand { get; set; }

    private void ShowAnnouncementsNow(object _)
    {
        new Thread(async () =>
        {
            try
            {
                await AnouncementManager.CheckNewAnnouncements();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"辣鸡公告系统又双叒叕崩了! {ex.Message}");
            }
        }).Start();
    }

    private async void OpenDebugTool(object _)
    {
        ++_consolesCount;

        var port = Program.Config.Web.DebugServicesServerPort;
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

                        var location = $"{nameof(Settings_UpdateViewModel)}.{nameof(OpenDebugTool)}";
                        Log.Warning(ex, $"In {location}: {ex.Message}");
                    }
                }

                async void Writer(StreamWriter writer)
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

                console.HandleMessages(Reader, Writer);
            }
            catch (Exception ex)
            {
                console.Dispose();
                var location = $"{nameof(Settings_GeneralViewModel)}.{nameof(OpenDebugTool)}";
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        }).Start();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
}

//                        :oooo
//                         YAAAAAAs_
//                 'AA.    ' AAAAAAAAs
//                  !AAAA_   ' AAAAAAAAs
//                    VAAAAA_.   AAAAAAAAs
//                     !AAAAAAAA_  AAAAAAAb
//                       VVAAAAAAA\/VAAAAAAb
//                         'VVAAAAAAAXXAAAAAb
//                             ~~VAAAAAAAAAABb
//                                   ~~~VAAAAB__
//                                     ,AAAAAAAAA_
//                                   ,AAAAAAAAA(*)AA_
//              _nnnnnnnnnnnnnnmmnnAAAAAAAAAAAAA8GAAAAn_
//          ,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAo
//        ,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAf~""
//       ,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA)
//      iAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP
//      AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
//     ,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA]
//     [AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA]
//     [AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
//     [AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!
//      AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA~
//      YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA`
//   __.'YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.
//  [AAAAA8AAAAAAAAAAAAAAAAAAAAAAAAAA~AAAAA_
//  (AAAAAAAAAAAAAAAAAAAAAAAAAAAAVf`   YAAAA]
//   VAAAAAAAAAAAAAAAAAAAAAAAAAAA_      AAAAAAAs
//     'VVVVVVVVVVVVVVVVVVVVVVVVVV+      !VVVVVVV
