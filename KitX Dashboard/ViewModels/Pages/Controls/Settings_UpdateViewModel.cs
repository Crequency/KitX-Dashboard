using Avalonia;
using Avalonia.Metadata;
using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using Common.Update.Checker;
using KitX.Dashboard.Converters;
using KitX.Dashboard.Network.DevicesNetwork;
using KitX.Dashboard.Services;
using KitX.Shared.Device;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Component = KitX.Dashboard.Models.Component;
using Timer = System.Timers.Timer;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_UpdateViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private bool _canUpdateDataGridView = true;

    internal Settings_UpdateViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        CheckUpdateCommand = ReactiveCommand.Create(CheckUpdate);

        UpdateCommand = ReactiveCommand.Create(Update);
    }

    public override void InitEvents()
    {
        Components.CollectionChanged += (_, _) =>
        {
            if (_canUpdateDataGridView)
            {
                CanUpdateCount = Components.Count(x => x.CanUpdate);
                PropertyChanged?.Invoke(this, new(nameof(ComponentsCount)));
            }
        };
    }

    private int canUpdateCount = 0;

    internal int CanUpdateCount
    {
        get => canUpdateCount;
        set
        {
            canUpdateCount = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(CanUpdateCount))
            );
        }
    }

    internal static int ComponentsCount { get => Components.Count; }

    internal static ObservableCollection<Component> Components { get; } = [];

    private string? tip = string.Empty;

    internal string? Tip
    {
        get => tip;
        set
        {
            tip = value;
            PropertyChanged?.Invoke(this, new(nameof(Tip)));
        }
    }

    private string diskUseStatus = string.Empty;

    internal string DiskUseStatus
    {
        get => diskUseStatus;
        set
        {
            diskUseStatus = value;
            PropertyChanged?.Invoke(this, new(nameof(DiskUseStatus)));
        }
    }

    public static int UpdateChannel
    {
        get => Instances.ConfigManager.AppConfig.Web.UpdateChannel switch
        {
            "stable" => 0,
            "beta" => 1,
            "alpha" => 2,
            _ => 0
        };
        set
        {
            Instances.ConfigManager.AppConfig.Web.UpdateChannel = value switch
            {
                0 => "stable",
                1 => "beta",
                2 => "alpha",
                _ => "stable"
            };

            SaveAppConfigChanges();
        }
    }

    private static string GetUpdateTip(string key) => Translate(key, prefix: "Text_Settings_Update_Tip_") ?? string.Empty;

    private static async void DownloadNewComponent(string url, string to, HttpClient client)
    {
        try
        {
            byte[] bytes = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(to, bytes);
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    internal ReactiveCommand<Unit, Unit>? CheckUpdateCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? UpdateCommand { get; set; }

    private static string GetDisplaySize(long size)
    {
        if (size / (1024 * 1024) > 2000) return $"{size / (1024 * 1024 * 1024)} GB";
        else if (size / 1024 > 1200) return $"{size / (1024 * 1024)} MB";
        else return $"{size / 1024} KB";
    }

    private Checker ScanComponents(string workbase)
    {
        var wd = workbase;
        var ld = Path.GetFullPath(ConstantTable.LanguageFilePath);

        var checker = new Checker()
            .SetRootDirectory(wd)
            .SetPerThreadFilesCount(Instances.ConfigManager.AppConfig.IO.UpdatingCheckPerThreadFilesCount)
            .SetTransHash2String(true)
            .AppendIgnoreFolder("Config")
            .AppendIgnoreFolder("Core")
            .AppendIgnoreFolder("Data")
            .AppendIgnoreFolder("Languages")
            .AppendIgnoreFolder("Log")
            .AppendIgnoreFolder("Update")
            .AppendIgnoreFolder("Loaders")
            .AppendIgnoreFolder("Plugins")
            .AppendIgnoreFolder(Instances.ConfigManager.AppConfig.App.LocalPluginsFileFolder)
            .AppendIgnoreFolder(Instances.ConfigManager.AppConfig.App.LocalPluginsDataFolder);

        foreach (var item in Instances.ConfigManager.AppConfig.App.SurpportLanguages)
            _ = checker.AppendIncludeFile($"{ld}/{item.Key}.axaml");

        Tip = GetUpdateTip("Scan");

        checker.Scan();

        return checker;
    }

    private void CalculateComponentsHash(Checker checker)
    {
        var location = $"{nameof(Settings_UpdateViewModel)}.{nameof(CalculateComponentsHash)}";

        var _calculateFinished = false;

        var timer = new Timer()
        {
            Interval = 10,
            AutoReset = true
        };
        timer.Elapsed += (_, _) =>
        {
            try
            {
                var progress = checker.GetProgress();

                Tip = GetUpdateTip("Calculate").Replace(
                    "%Progress%",
                    $"({progress.Item1}/{progress.Item2})"
                );

                if (_calculateFinished) timer.Stop();
            }
            catch (Exception e)
            {
                Log.Warning(e, $"In {location}: {e.Message}");
            }
        };
        timer.Start();

        checker.Calculate();

        _calculateFinished = true;
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNamingPolicy = new UpdateHashNamePolicy(),
    };

    private static async Task<Dictionary<string, (string, string, long)>?> GetLatestComponentsAsync
    (
        HttpClient client
    )
    {
        client.DefaultRequestHeaders.Accept.Clear();    //  清除请求头部

        var link = "https://" +
            Instances.ConfigManager.AppConfig.Web.UpdateServer +
            Instances.ConfigManager.AppConfig.Web.UpdatePath.Replace(
                "%platform%",
                DevicesDiscoveryServer.DefaultDeviceInfo.DeviceOSType switch
                {
                    OperatingSystems.Windows => "win",
                    OperatingSystems.Linux => "linux",
                    OperatingSystems.MacOS => "mac",
                    _ => ""
                }
            ) +
            $"{Instances.ConfigManager.AppConfig.Web.UpdateChannel}/" +
            Instances.ConfigManager.AppConfig.Web.UpdateSource;

        var json = await client.GetStringAsync(link);

        var latestComponents = JsonSerializer
            .Deserialize<Dictionary<string, (string, string, long)>>(
                json,
                JsonSerializerOptions
            );

        return latestComponents;
    }

    private void AddLocalComponentsToView
    (
        Dictionary<string, (string, string)> result,
        string wd,
        ref long localComponentsTotalSize
    )
    {
        var localComponents = new List<Component>();

        foreach (var item in result)
        {
            var size = new FileInfo($"{wd}/{item.Key}".GetFullPath()).Length;

            localComponentsTotalSize += size;

            localComponents.Add(new()
            {
                CanUpdate = false,
                Name = item.Key,
                MD5 = item.Value.Item1.ToUpper(),
                SHA1 = item.Value.Item2.ToUpper(),
                Size = GetDisplaySize(size),
            });
        }

        Dispatcher.UIThread.Post(() =>
        {
            var index = 0;

            foreach (var item in localComponents)
            {
                ++index;

                if (index == localComponents.Count) _canUpdateDataGridView = true;

                Components.Add(item);
            }
        });

        Tip = GetUpdateTip("Compare");

        while (Components.Count != result.Count) { }    //  阻塞直到前台加载完毕
    }

    private static object CompareDifferentComponents
    (
        ref string wd,
        ref Dictionary<string, (string, string, long)> latestComponents,
        ref Dictionary<string, (string, string)> result,
        ref long latestComponentsTotalSize
    )
    {
        var updatedComponents = new Dictionary<string, long>();
        var new2addComponents = new Dictionary<string, long>();
        var tdeleteComponents = new Dictionary<string, long>();

        foreach (var component in latestComponents)
        {
            if (result.TryGetValue(component.Key, out var current))
            {
                if (!current.Item1.ToUpper().Equals(component.Value.Item1.ToUpper()) ||
                    !current.Item2.ToUpper().Equals(component.Value.Item2.ToUpper()))
                    updatedComponents.Add(component.Key, component.Value.Item3);
            }
            else
            {
                new2addComponents.Add(component.Key, component.Value.Item3);
            }

            latestComponentsTotalSize += component.Value.Item3;
        }

        foreach (var item in result)
            if (!latestComponents.ContainsKey(item.Key))
                tdeleteComponents.Add(
                    item.Key,
                    new FileInfo(
                        $"{wd}/{item.Key}".GetFullPath()
                    ).Length
                );

        return (updatedComponents, new2addComponents, tdeleteComponents);
    }

    private void UpdateFrontendViewAfterCompare
    (
        Dictionary<string, long> updatedComponents,
        Dictionary<string, long> new2addComponents,
        Dictionary<string, long> tdeleteComponents,
        ref Dictionary<string, (string, string, long)> latestComponents,
        ref long localComponentsTotalSize,
        ref long latestComponentsTotalSize,
        ref Dictionary<string, (string, string)> result
    )
    {
        _canUpdateDataGridView = false;

        foreach (var item in Components)
        {
            if (item.Name is null) continue;

            if (updatedComponents.ContainsKey(item.Name))
            {
                item.CanUpdate = true;
                item.Task = Translate("Text_Public_Replace");
            }
            else if (tdeleteComponents.ContainsKey(item.Name))
            {
                item.CanUpdate = true;
                item.Task = Translate("Text_Public_Delete");
            }
        }

        var newComponents = new List<Component>();

        foreach (var item in new2addComponents)
        {
            newComponents.Add(new Component()
            {
                Name = item.Key,
                CanUpdate = true,
                MD5 = latestComponents[item.Key].Item1,
                SHA1 = latestComponents[item.Key].Item2,
                Task = Translate("Text_Public_Add"),
                Size = GetDisplaySize(item.Value)
            });
        }

        Dispatcher.UIThread.Post(() =>
        {
            var index = 0;

            foreach (var item in newComponents)
            {
                ++index;

                if (index == new2addComponents.Count) _canUpdateDataGridView = true;

                Components.Add(item);
            }
        });

        if (new2addComponents.Count == 0)
        {
            CanUpdateCount = Components.Count(x => x.CanUpdate);

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Components))
            );
        }

        while (Components.Count != result.Count + new2addComponents.Count) { }

        DiskUseStatus =
            localComponentsTotalSize > latestComponentsTotalSize
            ?
            $"- {GetDisplaySize(localComponentsTotalSize - latestComponentsTotalSize)}"
            :
            $"+ {GetDisplaySize(latestComponentsTotalSize - localComponentsTotalSize)}"
        ;
    }

    private void DownloadNewComponents
    (
        ref Dictionary<string, long> updatedComponents,
        ref HttpClient client
    )
    {
        Tip = GetUpdateTip("Download");

        //TODO: 下载有变更的文件
        var downloadLinkBase = "https://" +
            Instances.ConfigManager.AppConfig.Web.UpdateServer +
            Instances.ConfigManager.AppConfig.Web.UpdateDownloadPath.Replace(
                "%platform%",
                DevicesDiscoveryServer.DefaultDeviceInfo.DeviceOSType switch
                {
                    OperatingSystems.Windows => "win",
                    OperatingSystems.Linux => "linux",
                    OperatingSystems.MacOS => "mac",
                    _ => ""
                }) +
            $"{Instances.ConfigManager.AppConfig.Web.UpdateChannel}/";

        if (!Directory.Exists(ConstantTable.UpdateSavePath.GetFullPath()))
            Directory.CreateDirectory(ConstantTable.UpdateSavePath.GetFullPath());

        foreach (var item in updatedComponents)
            DownloadNewComponent(
                $"{downloadLinkBase}{item.Key.Replace(@"\", "/")}",
                $"{ConstantTable.UpdateSavePath}{item}".GetFullPath(),
                client
            );
    }

    private bool isCheckingOrUpdating = false;

    internal bool IsCheckingOrUpdating
    {
        get => isCheckingOrUpdating;
        set
        {
            isCheckingOrUpdating = value;

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsCheckingOrUpdating))
            );
        }
    }

    public void CheckUpdate()
    {
        var location = $"{nameof(Settings_UpdateViewModel)}.{nameof(CheckUpdate)}";

        Components.Clear();

        IsCheckingOrUpdating = true;

        _canUpdateDataGridView = false;

        Tip = GetUpdateTip("Start");

        new Thread(async () =>
        {
            try
            {
                var wd = Path.GetFullPath("./");

                if (wd is null)
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        await MessageBoxManager.GetMessageBoxStandard(
                            "Error",
                            "Can't get working directory!",
                            ButtonEnum.Ok,
                            Icon.Warning
                        ).ShowAsync();
                    });

                    IsCheckingOrUpdating = false;

                    return;
                }

                var checker = ScanComponents(wd);

                CalculateComponentsHash(checker);

                var result = checker.GetCalculateResult()
                    .OrderBy(
                        x =>
                            x.Key.Contains('/') ||
                            x.Key.Contains('\\') ? $"0{x.Key}" : x.Key
                    ).ToDictionary(
                        x => x.Key,
                        x => x.Value
                    );

                checker.CleanMemoryUsage();

                var localComponentsTotalSize = 0L;
                var latestComponentsTotalSize = 0L;

                AddLocalComponentsToView(result, wd, ref localComponentsTotalSize);

                var client = new HttpClient();

                var latestComponents
                    = await GetLatestComponentsAsync(client);

                if (latestComponents is not null)
                {
                    var difference =
                    ((
                        Dictionary<string, long>,
                        Dictionary<string, long>,
                        Dictionary<string, long>
                    ))
                    CompareDifferentComponents
                    (
                        ref wd, ref latestComponents, ref result,
                        ref latestComponentsTotalSize
                    );

                    var updatedComponents = difference.Item1;
                    var new2addComponents = difference.Item2;
                    var tdeleteComponents = difference.Item3;

                    UpdateFrontendViewAfterCompare
                    (
                        updatedComponents,
                        new2addComponents,
                        tdeleteComponents,
                        ref latestComponents,
                        ref localComponentsTotalSize,
                        ref latestComponentsTotalSize,
                        ref result
                    );

                    DownloadNewComponents(ref updatedComponents, ref client);

                    Tip = GetUpdateTip("Prepared");
                }

                client.Dispose();

                IsCheckingOrUpdating = false;

                GC.Collect();
            }
            catch (Exception e)
            {
                Tip = GetUpdateTip("Failed");

                IsCheckingOrUpdating = false;

                Dispatcher.UIThread.Post(async () =>
                {
                    await MessageBoxManager.GetMessageBoxStandard(
                        GetUpdateTip("Failed"),
                        e.Message,
                        ButtonEnum.Ok,
                        Icon.Error
                    ).ShowAsync();
                });

                Log.Error(e, $"In {location}: {e.Message}");
            }
        }).Start();
    }

    [DependsOn(nameof(IsCheckingOrUpdating))]
    internal bool CanCheckUpdate(object _) => !IsCheckingOrUpdating;

    public void Update()
    {
    }

    [DependsOn(nameof(CanUpdateCount))]
    [DependsOn(nameof(IsCheckingOrUpdating))]
    internal bool CanUpdate(object _) => CanUpdateCount > 0 && !IsCheckingOrUpdating;
}
